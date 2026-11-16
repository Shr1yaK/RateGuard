using System.ComponentModel.DataAnnotations;

namespace RateGuard.Api.RateLimiting;

/// <summary>
/// The two numbers that define a token bucket. Used in appsettings.json and as
/// the request body of PUT /api/limits/{clientId}, so both share one set of rules.
/// </summary>
public sealed class RateLimitPolicy
{
    [Range(1, 1_000_000)]
    public int Capacity { get; set; }

    [Range(0.01, 1_000_000)]
    public double RefillRatePerSecond { get; set; }

    /// <summary>Checks the [Range] attributes above.</summary>
    public static bool IsValid(RateLimitPolicy? policy) =>
        policy is not null &&
        Validator.TryValidateObject(policy, new ValidationContext(policy), null, validateAllProperties: true);
}
