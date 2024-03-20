using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Yafp;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Hosting;

public static class YafpWebHostBuilderExtensions
{
    public static IWebHostBuilder UseYafpForwardProxy(this IWebHostBuilder builder, Action<IYafpForwardProxyBuilder>? options = default)
    {
        builder.ConfigureServices(services =>
        {
            services.AddReverseProxy();

            services.AddOptions<YafpOptions>()
                .BindConfiguration("Yafp");
            services.AddSingleton<YafpCertificateProvider>();
            services.AddHostedService<HttpProxyListener>();
        });
        builder.ApplyKestrelConfiguration();

        var proxyBuilder = new YafpForwardProxyBuilder(builder);
        options?.Invoke(proxyBuilder);

        return builder;
    }

    private static IWebHostBuilder ApplyKestrelConfiguration(this IWebHostBuilder builder)
    {
        var currentId = Guid.NewGuid();
        var httpPipeOrSocketName = $"yafp-http-{currentId}";
        var httpsPipeOrSocketName = $"yafp-https-{currentId}";

        builder.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                options.ListenNamedPipe(httpPipeOrSocketName);
                options.ListenNamedPipe(httpsPipeOrSocketName, listenOptions => listenOptions.UseHttpsForYafp());
            }
            else
            {
                options.ListenUnixSocket(Path.Combine(Path.GetTempPath(), httpPipeOrSocketName));
                options.ListenUnixSocket(Path.Combine(Path.GetTempPath(), httpsPipeOrSocketName), listenOptions => listenOptions.UseHttpsForYafp());
            }
        });

        return builder;
    }

    private static void UseHttpsForYafp(this ListenOptions options)
    {
        var certProvider = options.ApplicationServices.GetRequiredService<YafpCertificateProvider>();
        options.UseHttps(httpsOptions =>
        {
            httpsOptions.ServerCertificateSelector = (context, host)
                => (host is null) ? null : certProvider.GetCertificateForHost(host);
        });
    }
}
