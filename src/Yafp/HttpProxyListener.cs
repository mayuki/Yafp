using Microsoft.Extensions.Hosting;
using System.Buffers;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Yafp;

public class HttpProxyListener : BackgroundService
{
    private readonly TaskCompletionSource _applicationStarted = new();
    private readonly IServer _server;
    private readonly YafpOptions _options;
    private readonly ILogger _logger;

    private readonly IPAddress _listenAddress;
    private readonly int _listenPort;

    private Func<CancellationToken, Task<Stream>>? _upstreamFactoryHttp;
    private Func<CancellationToken, Task<Stream>>? _upstreamFactoryHttps;

    public HttpProxyListener(IOptions<YafpOptions> options, IServer server, IHostApplicationLifetime applicationLifetime, ILogger<HttpProxyListener> logger)
    {
        _options = options.Value;
        _server = server;
        _logger = logger;
        applicationLifetime.ApplicationStarted.Register(() => _applicationStarted.SetResult());

        _listenAddress = _options.ListenAddress is not null ? IPAddress.Parse(_options.ListenAddress) : IPAddress.Loopback;
        _listenPort = _options.ListenPort;
        _server.Features.Set(new YafpProxyAddressFeature(_listenAddress, _listenPort));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // NOTE: Wait for Kestrel to start up.
        await _applicationStarted.Task;
        
        var serverAddress = _server.Features.GetRequiredFeature<IServerAddressesFeature>();
        var httpAddress = serverAddress.Addresses.First(x => x.StartsWith("http://pipe") || x.StartsWith("http://unix"));
        var httpsAddress = serverAddress.Addresses.First(x => x.StartsWith("https://pipe") || x.StartsWith("https://unix"));
        _upstreamFactoryHttp = CreateStreamFactory(httpAddress);
        _upstreamFactoryHttps = CreateStreamFactory(httpsAddress);

        await ListenProxyPortAsync(stoppingToken);
    }

    private static Func<CancellationToken, Task<Stream>> CreateStreamFactory(string address)
    {
        var indexSchemeEnd = address.IndexOf("//", StringComparison.Ordinal) + 2;
        var addressWithoutScheme = address.Substring(indexSchemeEnd);
        if (addressWithoutScheme.StartsWith("pipe:"))
        {
            var pipeName = addressWithoutScheme.Substring(6); // pipe:/<PipeName>
            return async (cancellationToken) =>
            {
                var upstream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, System.IO.Pipes.PipeOptions.WriteThrough | System.IO.Pipes.PipeOptions.Asynchronous);
                await upstream.ConnectAsync(cancellationToken);
                return upstream;
            };
        }
        else if (addressWithoutScheme.StartsWith("unix:")) // unix:/Path/To/UDS or unix:C:\Path\To\UDS
        {
            var path = addressWithoutScheme.Substring(5);
            return async (cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken);
                var upstream = new NetworkStream(socket, ownsSocket: true);
                return upstream;
            };
        }
        else
        {
            throw new InvalidOperationException($"Unsupported Endpoint: {address}");
        }
    }


    private async Task ListenProxyPortAsync(CancellationToken stoppingToken)
    {
        using var listener = new TcpListener(_listenAddress, _listenPort);
        listener.Start();
        _logger.LogInformation($"Now proxy server is listening on: {_listenAddress}:{_listenPort}");

        if (_upstreamFactoryHttp is null) throw new InvalidOperationException("_upstreamFactoryHttp is not configured");
        if (_upstreamFactoryHttps is null) throw new InvalidOperationException("_upstreamFactoryHttps is not configured");

        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stoppingToken);
            _ = Task.Run(async () =>
            {
                using var downstream = client.GetStream();
                var pipeReader = PipeReader.Create(downstream);

                var result = await TryReadRequestLineAsync(pipeReader, stoppingToken);
                if (result is null)
                {
                    pipeReader.Complete();
                    return;
                }

                if (result.Value.IsConnect)
                {
                    // CONNECT
                    // Client (downstream) --[HTTPS]--> Kestrel (upstream)
                    downstream.Write("HTTP/1.0 200 OK\r\n\r\n"u8);
                    await downstream.FlushAsync(stoppingToken);
                    using var upstream = await _upstreamFactoryHttps(stoppingToken);
                    var copyToUpstreamTask = pipeReader.CopyToAsync(upstream, stoppingToken);
                    var copyToDownstreamTask = upstream.CopyToAsync(downstream, stoppingToken);
                    await Task.WhenAll(copyToDownstreamTask, copyToUpstreamTask);
                }
                else
                {
                    // Client (downstream) --[HTTP]--> Kestrel (upstream)
                    using var upstream = await _upstreamFactoryHttp(stoppingToken);
                    var copyToUpstreamTask = pipeReader.CopyToAsync(upstream, stoppingToken);
                    var copyToDownstreamTask = upstream.CopyToAsync(downstream, stoppingToken);
                    await Task.WhenAll(copyToDownstreamTask, copyToUpstreamTask);
                }
            });
        }
    }

    private static async Task<ReadRequestLineResult?> TryReadRequestLineAsync(PipeReader pipeReader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await pipeReader.ReadAsync(cancellationToken);
            if (TryReadRequestLineAndHeaders(result.Buffer, out var requestLine))
            {
                pipeReader.AdvanceTo(result.Buffer.GetPosition(requestLine.ReadLength));
                return requestLine;
            }
            pipeReader.AdvanceTo(result.Buffer.GetPosition(0));

            if (result.IsCanceled || result.IsCompleted)
            {
                return null;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        return null;

        static bool TryReadRequestLineAndHeaders(ReadOnlySequence<byte> buffer, out ReadRequestLineResult result)
        {
            var reader = new SequenceReader<byte>(buffer);
            if (reader.TryReadTo(out ReadOnlySpan<byte> requestLine, "\r\n"u8))
            {
                // 1st line (Request Line)
                var indexMethodEnd = requestLine.IndexOf((byte)' ');
                if (indexMethodEnd > 0)
                {
                    // Method
                    var method = requestLine.Slice(0, indexMethodEnd);
                    if (method.SequenceEqual("CONNECT"u8))
                    {
                        // Authority
                        var indexAuthorityEnd = requestLine.Slice(indexMethodEnd + 1).IndexOf((byte)' ');
                        if (indexAuthorityEnd > 0)
                        {
                            var authority = requestLine.Slice(indexMethodEnd + 1, indexAuthorityEnd);
                            var portIndex = authority.IndexOf(":"u8);
                            if (portIndex > -1)
                            {
                                // Consume remaining request headers.
                                if (reader.TryReadTo(out ReadOnlySpan<byte> tmp, "\r\n\r\n"u8))
                                {
                                    result = new ReadRequestLineResult(IsConnect: true, Encoding.UTF8.GetString(authority.Slice(0, portIndex)), int.Parse(authority.Slice(portIndex + 1)), reader.Consumed);
                                    return true;
                                }
                            }
                        }
                    }
                }
                result = default;
                return true;
            }
            else
            {
                // Insufficient data
                result = default;
                return false;
            }
        }
    }

    private readonly record struct ReadRequestLineResult(bool IsConnect, string? Host, int? Port, long ReadLength);
}