namespace BlazorIntegration.Recording;

public record RequestResponseEvent(int Id, DateTimeOffset Timestamp, string Url, int StatusCode, string? ContentType);
