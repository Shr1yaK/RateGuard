using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace RateGuard.Api.RateLimiting;

/// <summary>
/// Keeps one TokenBucket per client in process memory.
///
/// Registered as a singleton: every request must see the same buckets, otherwise
/// each request would get a new, full bucket and nothing would ever be limited.
/// State is lost on restart and is not shared between servers.
/// </summary>
public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly ConcurrentDictionary<string, RateLimitPolicy> _policies;
    private readonly RateLimitPolicy _defaultPolicy;
    private readonly TimeProvider _timeProvider;

    public InMemoryRateLimitStore(IOptions<RateLimitOptions> options, TimeProvider timeProvider)
    {
        _defaultPolicy = options.Value.Default;
        _policies = new ConcurrentDictionary<string, RateLimitPolicy>(options.Value.Clients);
        _timeProvider = timeProvider;
    }

    public TokenBucket GetOrCreateBucket(string clientId) =>
        _buckets.GetOrAdd(clientId, CreateBucket);

    public TokenBucket? FindBucket(string clientId) =>
        _buckets.TryGetValue(clientId, out var bucket) ? bucket : null;

    public void SetPolicy(string clientId, RateLimitPolicy policy)
    {
        _policies[clientId] = policy;
        // Replace rather than mutate: the new bucket starts full under the new policy.
        _buckets[clientId] = CreateBucket(clientId);
    }

    private TokenBucket CreateBucket(string clientId)
    {
        var policy = _policies.GetValueOrDefault(clientId, _defaultPolicy);
        return new TokenBucket(policy.Capacity, policy.RefillRatePerSecond, _timeProvider);
    }
}
