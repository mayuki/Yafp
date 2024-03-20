using Yafp;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class YafpWebApplicationExtensions
{
    public static void MapForwardProxy(this WebApplication app)
    {
        app.UseMiddleware<ForwardProxyMiddleware>();
    }
}