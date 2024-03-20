using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Yafp;

internal class ForwardProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpTransformer? _transformer;
    private readonly HttpMessageInvoker _httpInvoker;

    public ForwardProxyMiddleware(RequestDelegate next, IServiceProvider serviceProvider, YafpForwardProxyBuildContext buildContext)
    {
        _next = next;
        _transformer = buildContext.TransformerFactory?.Invoke(serviceProvider);
        _httpInvoker = new HttpMessageInvoker(new SocketsHttpHandler()
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        });
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // If the local/remote IP address is not null, the client is connected directly to the server using HTTP/HTTPS.
        // Do not forward the requests to prevent forwarding loop infinitely.
        if (context.Connection.LocalIpAddress is not null)
        {
            await _next(context);
            return;
        }

        var httpForwarder = context.RequestServices.GetRequiredService<IHttpForwarder>();
        var prefix = UriHelper.BuildAbsolute(context.Request.Scheme, context.Request.Host);
        context.Response.Headers.TryAdd("x-yafp-enabled", "1");

        await httpForwarder.SendAsync(context, prefix, _httpInvoker, ForwarderRequestConfig.Empty, _transformer ?? HttpTransformer.Empty);
    }
}