namespace RateGuard.Api.RateLimiting;

/// <summary>
/// A consistent view of a bucket, taken while holding the bucket's lock.
/// </summary>
public readonly record struct BucketSnapshot(
    int Capacity,
    double RefillRatePerSecond,
    int AvailableTokens,
    TimeSpan RetryAfter);

/// <summary>
/// Token bucket rate limiter.
///
/// The bucket holds up to <see cref="Capacity"/> tokens and each request costs one.
/// Tokens refill continuously at <see cref="RefillRatePerSecond"/>. Refill is lazy:
/// it is computed from the time elapsed since the last refill whenever the bucket
/// is touched, so no background timer is needed.
///
/// A client can burst up to Capacity requests, and its sustained rate is capped
/// at RefillRatePerSecond requests per second.
/// </summary>
public sealed class TokenBucket
{
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();

    // Fractional on purpose: half a second at 1 token/sec leaves 0.5 tokens.
    private double _tokens;
    private long _lastRefillTimestamp;

    public TokenBucket(int capacity, double refillRatePerSecond, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillRatePerSecond);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Capacity = capacity;
        RefillRatePerSecond = refillRatePerSecond;
        _timeProvider = timeProvider;

        _tokens = capacity; // start full
        _lastRefillTimestamp = timeProvider.GetTimestamp();
    }

    public int Capacity { get; }

    public double RefillRatePerSecond { get; }

    /// <summary>Tries to take one token. Returns true if the request is allowed.</summary>
    public bool TryConsume() => TryConsume(out _);

    /// <summary>
    /// Tries to take one token, and also returns the bucket state right after the
    /// attempt. Both happen under one lock, so the snapshot matches the decision.
    /// </summary>
    public bool TryConsume(out BucketSnapshot snapshot)
    {
        lock (_lock)
        {
            Refill();

            bool allowed = _tokens >= 1;
            if (allowed)
            {
                _tokens -= 1;
            }

            snapshot = CreateSnapshot();
            return allowed;
        }
    }

    /// <summary>Returns the current state without consuming a token.</summary>
    public BucketSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            Refill();
            return CreateSnapshot();
        }
    }

    // Caller must hold _lock.
    private void Refill()
    {
        long now = _timeProvider.GetTimestamp();
        double elapsedSeconds = _timeProvider.GetElapsedTime(_lastRefillTimestamp, now).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return;
        }

        _tokens = Math.Min(Capacity, _tokens + elapsedSeconds * RefillRatePerSecond);
        _lastRefillTimestamp = now;
    }

    // Caller must hold _lock.
    private BucketSnapshot CreateSnapshot()
    {
        TimeSpan retryAfter = _tokens >= 1
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((1 - _tokens) / RefillRatePerSecond);

        return new BucketSnapshot(
            Capacity,
            RefillRatePerSecond,
            AvailableTokens: (int)Math.Floor(_tokens),
            retryAfter);
    }
}
