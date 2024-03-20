using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseYafpForwardProxy(options =>
{
    options.UseTransformer(_ => new MyResponseTransformer());
});
builder.WebHost.ConfigureKestrel(options =>
{
    // If used as a web server as well as a proxy, the port must be listened to manually
    options.ListenLocalhost(15050);
});

var app = builder.Build();
app.Use((context, next) =>
{
    // Fixed response
    if (context.Request.Host.Host == "www.example.local")
    {
        context.Response.Headers.ContentType = "text/plain";
        context.Response.WriteAsync("Hello!");
        return Task.CompletedTask;
    }

    return next(context);
});
app.MapForwardProxy();
app.MapGet("/", () => Results.Content("Hello!"));
app.Run();

//    ┌──────┐         ┌──────────┐                                    ┌──────┐
//    │      │         │          │ ────► TransformRequestAsync  ────► │      │
//    │      │ ──────► │          │                                    │      │
//    │Client│         │Middleware│                                    │Server│
//    │      │ ◄────── │          │                                    │      │
//    │      │         │          │ ◄──── TransformResponseAsync ◄──── │      │
//    └──────┘         └──────────┘                                    └──────┘

class MyResponseTransformer : HttpTransformer
{
    // Before sending the request to the remote server.
    public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
    {
        // NOTE: We need calling base method to copy headers.
        // https://github.com/microsoft/reverse-proxy/blob/e672f151e1a1104a849bb24d9be94bf30dac63c9/src/ReverseProxy/Transforms/Builder/StructuredTransformer.cs#L80
#pragma warning disable CA2016 // Forward the 'CancellationToken' parameter to methods
#pragma warning disable CS0618 // Type or member is obsolete
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix);
#pragma warning restore CS0618
#pragma warning restore CA2016

        proxyRequest.Headers.TryAddWithoutValidation("x-yafp-request", "1");
    }

    // Before the response is received from th remote server and returned to the client
    public override async ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
    {
        // NOTE: We need calling base method to copy headers.
        // https://github.com/microsoft/reverse-proxy/blob/e672f151e1a1104a849bb24d9be94bf30dac63c9/src/ReverseProxy/Transforms/Builder/StructuredTransformer.cs#L130
#pragma warning disable CA2016 // Forward the 'CancellationToken' parameter to methods
#pragma warning disable CS0618 // Type or member is obsolete
        await base.TransformResponseAsync(httpContext, proxyResponse);
#pragma warning restore CS0618
#pragma warning restore CA2016

        httpContext.Response.Headers["x-yafp-response"] = "hello";
        return true;
    }
}
