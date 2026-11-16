using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RateGuard.Tests;

/// <summary>
/// End-to-end tests through the full HTTP pipeline: middleware, routing, controllers.
/// xUnit creates a new instance of this class per test, so every test gets its
/// own app and its own empty store: no test can drain a bucket another test uses.
/// </summary>
public sealed class RateLimitingIntegrationTests : IDisposable
{
    private const int Capacity = RateGuardFactory.Capacity;

    private readonly RateGuardFactory _factory = new();
    private readonly HttpClient _client;

    public RateLimitingIntegrationTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Returns429AfterLimitIsReached()
    {
        for (int i = 0; i < Capacity; i++)
        {
            var ok = await GetResource("alice");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var rejected = await GetResource("alice");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task AllowedResponseHasRateLimitHeaders()
    {
        var response = await GetResource("alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Capacity.ToString(), Header(response, "X-RateLimit-Limit"));
        Assert.Equal((Capacity - 1).ToString(), Header(response, "X-RateLimit-Remaining"));
    }

    [Fact]
    public async Task RejectedResponseHasRetryAfterAndProblemJson()
    {
        await Drain("alice");

        var response = await GetResource("alice");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        // Empty bucket at 1 token/sec: the next token comes in 1 second.
        Assert.Equal("1", Header(response, "Retry-After"));
        Assert.Equal("0", Header(response, "X-RateLimit-Remaining"));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Too Many Requests", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task HealthIsNotRateLimited()
    {
        for (int i = 0; i < Capacity * 4; i++)
        {
            var response = await _client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("X-RateLimit-Limit"));
        }
    }

    [Fact]
    public async Task SwaggerIsNotRateLimited()
    {
        for (int i = 0; i < Capacity * 2; i++)
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task ClientsHaveSeparateBuckets()
    {
        await Drain("alice");

        var bob = await GetResource("bob");

        Assert.Equal(HttpStatusCode.OK, bob.StatusCode);
    }

    [Fact]
    public async Task TokensRefillAsTimePasses()
    {
        await Drain("alice");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetResource("alice")).StatusCode);

        _factory.Time.Advance(TimeSpan.FromSeconds(1)); // 1 token at 1 token/sec

        Assert.Equal(HttpStatusCode.OK, (await GetResource("alice")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetResource("alice")).StatusCode);
    }

    [Fact]
    public async Task GetLimitsReturns404ForUnknownClient()
    {
        var response = await Send(HttpMethod.Get, "/api/limits/nobody", apiKey: "admin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLimitsReturnsCurrentTokens()
    {
        await GetResource("alice");
        await GetResource("alice");

        var response = await Send(HttpMethod.Get, "/api/limits/alice", apiKey: "admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Capacity, body.RootElement.GetProperty("capacity").GetInt32());
        Assert.Equal(Capacity - 2, body.RootElement.GetProperty("availableTokens").GetInt32());
    }

    [Fact]
    public async Task PutLimitsAppliesNewPolicy()
    {
        var put = await Send(HttpMethod.Put, "/api/limits/alice", apiKey: "admin",
            body: new { capacity = 2, refillRatePerSecond = 1 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await GetResource("alice")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetResource("alice")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetResource("alice")).StatusCode);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 0)]
    [InlineData(-3, 1)]
    [InlineData(5, -1)]
    public async Task PutLimitsRejectsInvalidPolicy(int capacity, double rate)
    {
        var response = await Send(HttpMethod.Put, "/api/limits/alice", apiKey: "admin",
            body: new { capacity, refillRatePerSecond = rate });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> GetResource(string apiKey) =>
        Send(HttpMethod.Get, "/api/resource", apiKey);

    private async Task Drain(string apiKey)
    {
        for (int i = 0; i < Capacity; i++)
        {
            await GetResource(apiKey);
        }
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string apiKey, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Api-Key", apiKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return _client.SendAsync(request);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : null;
}
