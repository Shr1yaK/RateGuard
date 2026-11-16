namespace RateGuard.Api.RateLimiting;

/// <summary>
/// Where buckets live. The middleware and controllers only see this interface,
/// so the in-memory version could be swapped for a shared one (e.g. Redis)
/// without touching them.
/// </summary>
public interface IRateLimitStore
{
    /// <summary>Returns the client's bucket, creating a full one on first use.</summary>
    TokenBucket GetOrCreateBucket(string clientId);

    /// <summary>Returns the client's bucket, or null if the client has never been seen.</summary>
    TokenBucket? FindBucket(string clientId);

    /// <summary>Sets a custom policy for the client and gives it a fresh bucket.</summary>
    void SetPolicy(string clientId, RateLimitPolicy policy);
}
