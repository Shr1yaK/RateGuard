using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace RateGuard.Api.RateLimiting;

/// <summary>
/// Runs before every request (except /health and /swagger):
///   1. works out who the client is (X-Api-Key header, else remote IP),
///   2. takes one token from that client's bucket,
///   3. lets the request through, or answers 429 Too Many Requests.
/// </summary>
public sealed class RateLimitingMiddleware
{
    public const string ApiKeyHeader = "X-Api-Key";

    private static readonly PathString[] SkippedPaths = ["/health", "/swagger"];

    private readonly RequestDelegate _next;
    private readonly IRateLimitStore _store;

    // Constructor injection is safe here because the store is a singleton,
    // and middleware is created once for the app's lifetime.
    public RateLimitingMiddleware(RequestDelegate next, IRateLimitStore store)
    {
        _next = next;
        _store = store;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsSkipped(context.Request.Path))
        {
            await _next(context);
            return;
        }

        string clientId = GetClientId(context);
        TokenBucket bucket = _store.GetOrCreateBucket(clientId);
        bool allowed = bucket.TryConsume(out BucketSnapshot snapshot);

        // Set on every limited response, allowed or not, so clients can pace themselves.
        context.Response.Headers["X-RateLimit-Limit"] = ToHeader(snapshot.Capacity);
        context.Response.Headers["X-RateLimit-Remaining"] = ToHeader(snapshot.AvailableTokens);

        if (allowed)
        {
            await _next(context);
            return;
        }

        // Retry-After must be whole seconds, so round up (0.5s -> 1s), never down to 0.
        int retryAfterSeconds = (int)Math.Ceiling(snapshot.RetryAfter.TotalSeconds);

        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc6585#section-4",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = $"Rate limit of {snapshot.Capacity} requests exceeded. Retry after {retryAfterSeconds} second(s).",
            Instance = context.Request.Path,
        };
        problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = ToHeader(retryAfterSeconds);
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }

    private static bool IsSkipped(PathString path) =>
        SkippedPaths.Any(skipped => path.StartsWithSegments(skipped, StringComparison.OrdinalIgnoreCase));

    private static string GetClientId(HttpContext context)
    {
        string? apiKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey.Trim();
        }

        // No key: fall back to the caller's IP. It can be null (e.g. in-memory test server).
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string ToHeader(int value) => value.ToString(CultureInfo.InvariantCulture);
}
