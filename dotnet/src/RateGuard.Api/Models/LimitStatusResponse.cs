using RateGuard.Api.RateLimiting;

namespace RateGuard.Api.Models;

/// <summary>What GET/PUT /api/limits/{clientId} return.</summary>
public sealed record LimitStatusResponse(
    string ClientId,
    int Capacity,
    double RefillRatePerSecond,
    int AvailableTokens,
    double RetryAfterSeconds)
{
    public static LimitStatusResponse From(string clientId, BucketSnapshot snapshot) => new(
        clientId,
        snapshot.Capacity,
        snapshot.RefillRatePerSecond,
        snapshot.AvailableTokens,
        snapshot.RetryAfter.TotalSeconds);
}
