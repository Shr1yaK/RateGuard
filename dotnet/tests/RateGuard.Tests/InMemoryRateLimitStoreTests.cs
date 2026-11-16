using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RateGuard.Api.RateLimiting;

namespace RateGuard.Tests;

public class InMemoryRateLimitStoreTests
{
    private readonly InMemoryRateLimitStore _store;

    public InMemoryRateLimitStoreTests()
    {
        var options = new RateLimitOptions
        {
            Default = new RateLimitPolicy { Capacity = 10, RefillRatePerSecond = 2 },
            Clients =
            {
                ["premium"] = new RateLimitPolicy { Capacity = 50, RefillRatePerSecond = 10 },
            },
        };
        _store = new InMemoryRateLimitStore(Options.Create(options), new FakeTimeProvider());
    }

    [Fact]
    public void UnknownClientGetsDefaultPolicy()
    {
        var bucket = _store.GetOrCreateBucket("someone");

        Assert.Equal(10, bucket.Capacity);
        Assert.Equal(2, bucket.RefillRatePerSecond);
    }

    [Fact]
    public void ConfiguredClientGetsItsOverride()
    {
        var bucket = _store.GetOrCreateBucket("premium");

        Assert.Equal(50, bucket.Capacity);
        Assert.Equal(10, bucket.RefillRatePerSecond);
    }

    [Fact]
    public void SameClientAlwaysGetsTheSameBucket()
    {
        Assert.Same(_store.GetOrCreateBucket("a"), _store.GetOrCreateBucket("a"));
        Assert.NotSame(_store.GetOrCreateBucket("a"), _store.GetOrCreateBucket("b"));
    }

    [Fact]
    public void FindBucketReturnsNullForUnseenClient()
    {
        Assert.Null(_store.FindBucket("never-seen"));
    }

    [Fact]
    public void SetPolicyReplacesBucketWithAFullOneUnderTheNewPolicy()
    {
        var old = _store.GetOrCreateBucket("a");
        old.TryConsume();

        _store.SetPolicy("a", new RateLimitPolicy { Capacity = 3, RefillRatePerSecond = 1 });

        var current = _store.FindBucket("a");
        Assert.NotNull(current);
        Assert.NotSame(old, current);
        Assert.Equal(3, current.Capacity);
        Assert.Equal(3, current.GetSnapshot().AvailableTokens);
    }

    [Theory]
    [InlineData(10, 2, true)]
    [InlineData(0, 2, false)]
    [InlineData(10, 0, false)]
    [InlineData(2_000_000, 2, false)]
    public void PolicyValidationUsesRangeAttributes(int capacity, double rate, bool expected)
    {
        var policy = new RateLimitPolicy { Capacity = capacity, RefillRatePerSecond = rate };

        Assert.Equal(expected, RateLimitPolicy.IsValid(policy));
    }
}
