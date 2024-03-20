using BlazorIntegration.Components;
using BlazorIntegration.Recording;
using MessagePipe;
using Yafp;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseKestrel(options =>
{
    options.ListenLocalhost(15000);
});
builder.WebHost.UseYafpForwardProxy(options =>
{
    options.UseTransformer(sp => new RequestResponseRecordingTransformer(sp.GetRequiredService<IPublisher<RequestResponseEvent>>(), sp.GetRequiredService<ResponseCacheStore>()));
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMessagePipe();
builder.Services.AddSingleton<ResponseCacheStore>();

var app = builder.Build();

app.MapForwardProxy();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/_cache/{*uri}", (string uri, ResponseCacheStore responseCacheStore, HttpContext httpContext) =>
{
    if (httpContext.Request.QueryString.HasValue)
    {
        uri += httpContext.Request.QueryString.Value;
    }

    if (responseCacheStore.TryGet(uri, out var contentType, out var data))
    {
        return Results.Bytes(data, contentType);
    }

    return Results.NotFound();
});

app.Run();
