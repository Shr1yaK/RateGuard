using Microsoft.Extensions.Time.Testing;
using RateGuard.Api.RateLimiting;

namespace RateGuard.Tests;

public class TokenBucketTests
{
    // FakeTimeProvider's clock only moves when we call Advance(), so tests are
    // deterministic and never sleep.
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void StartsFull()
    {
        var bucket = new TokenBucket(capacity: 5, refillRatePerSecond: 1, _time);

        Assert.Equal(5, bucket.GetSnapshot().AvailableTokens);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(bucket.TryConsume());
        }
    }

    [Fact]
    public void RejectsOnceDrained()
    {
        var bucket = new TokenBucket(capacity: 3, refillRatePerSecond: 2, _time);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(bucket.TryConsume());
        }

        Assert.False(bucket.TryConsume(out var snapshot));
        Assert.Equal(0, snapshot.AvailableTokens);
        // Empty bucket at 2 tokens/sec: the next token arrives in 0.5s.
        Assert.Equal(TimeSpan.FromMilliseconds(500), snapshot.RetryAfter);
    }

    [Fact]
    public void RefillsOverTime()
    {
        var bucket = new TokenBucket(capacity: 3, refillRatePerSecond: 2, _time);
        Drain(bucket);

        _time.Advance(TimeSpan.FromMilliseconds(500)); // 0.5s * 2/s = 1 token

        Assert.True(bucket.TryConsume());
        Assert.False(bucket.TryConsume());
    }

    [Fact]
    public void PartialRefillIsNotEnoughForARequest()
    {
        var bucket = new TokenBucket(capacity: 3, refillRatePerSecond: 2, _time);
        Drain(bucket);

        _time.Advance(TimeSpan.FromMilliseconds(250)); // 0.5 tokens

        Assert.False(bucket.TryConsume(out var snapshot));
        // Half a token is missing, at 2 tokens/sec that is 0.25s.
        Assert.Equal(TimeSpan.FromMilliseconds(250), snapshot.RetryAfter);
    }

    [Fact]
    public void NeverExceedsCapacity()
    {
        var bucket = new TokenBucket(capacity: 4, refillRatePerSecond: 10, _time);
        bucket.TryConsume();

        _time.Advance(TimeSpan.FromHours(1)); // would be 36,000 tokens without the cap

        Assert.Equal(4, bucket.GetSnapshot().AvailableTokens);
    }

    [Fact]
    public void ConcurrentConsumptionNeverOverIssuesTokens()
    {
        const int capacity = 1_000;
        // The fake clock never advances here, so no refill happens: exactly
        // `capacity` calls may succeed, no matter how many threads race.
        var bucket = new TokenBucket(capacity, refillRatePerSecond: 1, _time);
        int allowed = 0;

        Parallel.For(0, 5_000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            if (bucket.TryConsume())
            {
                Interlocked.Increment(ref allowed);
            }
        });

        Assert.Equal(capacity, allowed);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(5, 0)]
    [InlineData(5, -2)]
    public void RejectsInvalidSettings(int capacity, double refillRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TokenBucket(capacity, refillRate, _time));
    }

    private static void Drain(TokenBucket bucket)
    {
        while (bucket.TryConsume())
        {
        }
    }
}
