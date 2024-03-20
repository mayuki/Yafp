using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Yafp;

public interface IYafpForwardProxyBuilder
{
    IYafpForwardProxyBuilder UseTransformer(Func<IServiceProvider, HttpTransformer> transformerFactory);
}

internal class YafpForwardProxyBuilder : IYafpForwardProxyBuilder
{
    private readonly YafpForwardProxyBuildContext _buildContext = new();
    private readonly IWebHostBuilder _webHostBuilder;

    public YafpForwardProxyBuilder(IWebHostBuilder builder)
    {
        _webHostBuilder = builder;
        _webHostBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(_buildContext);
        });
    }

    public IYafpForwardProxyBuilder UseTransformer(Func<IServiceProvider, HttpTransformer> transformerFactory)
    {
        _buildContext.TransformerFactory = transformerFactory;
        return this;
    }
}

internal class YafpForwardProxyBuildContext
{
    public Func<IServiceProvider, HttpTransformer?>? TransformerFactory { get; set; }
}