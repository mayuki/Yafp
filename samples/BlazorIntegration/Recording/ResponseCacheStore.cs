using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace BlazorIntegration.Recording;

public class ResponseCacheStore
{
    private readonly ConcurrentDictionary<string, (string ContentType, byte[] Data)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string uri, string contentType, byte[] data)
    {
        _cache[uri] = (contentType, data);
    }

    public bool TryGet(string uri, [NotNullWhen(true)] out string? contentType, [NotNullWhen(true)] out byte[]? data)
    {
        if (_cache.TryGetValue(uri, out var value))
        {
            contentType = value.ContentType;
            data = value.Data;
            return true;
        }

        contentType = null;
        data = null;
        return false;
    }
}