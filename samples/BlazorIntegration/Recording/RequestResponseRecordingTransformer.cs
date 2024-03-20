using MessagePipe;
using Microsoft.AspNetCore.Http.Extensions;
using Yarp.ReverseProxy.Forwarder;

namespace BlazorIntegration.Recording;

public class RequestResponseRecordingTransformer(IPublisher<RequestResponseEvent> publisher, ResponseCacheStore responseCacheStore) : HttpTransformer
{
    private int _responseCount = 0;

    public override async ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
    {
        var responseId = Interlocked.Increment(ref _responseCount);
        var uri = httpContext.Request.GetEncodedUrl();
        var contentType = proxyResponse?.Content.Headers.ContentType?.ToString();

        publisher.Publish(new RequestResponseEvent(responseId, DateTimeOffset.Now, uri, httpContext.Response.StatusCode, contentType));

        if (proxyResponse != null && contentType != null && contentType.StartsWith("image/"))
        {
            responseCacheStore.Add(uri, contentType, await proxyResponse.Content.ReadAsByteArrayAsync(cancellationToken));
        }

        return await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
    }
}