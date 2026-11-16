namespace RateGuard.Api.RateLimiting;

/// <summary>
/// Bound from the "RateLimiting" section of appsettings.json.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Policy for any client without an override.</summary>
    public RateLimitPolicy Default { get; set; } = new() { Capacity = 10, RefillRatePerSecond = 2 };

    /// <summary>Per-client overrides, keyed by client id (API key or IP).</summary>
    public Dictionary<string, RateLimitPolicy> Clients { get; set; } = new();
}
