using BlazorIntegration.Recording;
using MessagePipe;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Yafp;

namespace BlazorIntegration.Components.Pages;

partial class Home : IDisposable
{
    private IDisposable? _subscription;
    private Stack<RequestResponseEvent> _requestResponses = new();
    private YafpProxyAddressFeature? _yafpProxyAddress;

    [Inject]
    public required ISubscriber<RequestResponseEvent> RequestResponse { get; init; }
    [Inject]
    public required IServer Server { get; init; }

    protected override void OnInitialized()
    {
        _yafpProxyAddress = Server.Features.Get<YafpProxyAddressFeature>();
        _subscription = RequestResponse.Subscribe(x =>
        {
            _ = InvokeAsync(() =>
            {
                _requestResponses.Push(x);
                StateHasChanged();
            });
        });
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}