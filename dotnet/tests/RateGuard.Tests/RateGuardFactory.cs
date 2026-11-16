using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace RateGuard.Tests;

/// <summary>
/// Boots the real app in memory (no network, no ports) with two test changes:
///   - a small, explicit limit (5 tokens, 1 token/sec) so tests don't depend on appsettings.json,
///   - a FakeTimeProvider, so no tokens refill unless a test moves the clock.
/// Without the fake clock, a slow test machine could refill a token mid-test and make tests flaky.
/// </summary>
public sealed class RateGuardFactory : WebApplicationFactory<Program>
{
    public const int Capacity = 5;

    public FakeTimeProvider Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RateLimiting:Default:Capacity"] = Capacity.ToString(),
                ["RateLimiting:Default:RefillRatePerSecond"] = "1",
            }));

        // ConfigureTestServices runs after Program.cs registrations, so this replaces the real clock.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }
}
