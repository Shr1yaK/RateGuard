using RateGuard.Api.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Rate limiting settings from appsettings.json. ValidateOnStart makes a bad
// config (e.g. Capacity: 0) stop the app at startup instead of failing later.
builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .Validate(
        o => RateLimitPolicy.IsValid(o.Default) && o.Clients.Values.All(RateLimitPolicy.IsValid),
        "RateLimiting: every Capacity must be 1..1000000 and every RefillRatePerSecond 0.01..1000000.")
    .ValidateOnStart();

// The real clock in production; tests can replace it with a FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);

// Singleton: all requests must share the same buckets.
builder.Services.AddSingleton<IRateLimitStore, InMemoryRateLimitStore>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// First in the pipeline, so a rejected request costs as little work as possible.
app.UseMiddleware<RateLimitingMiddleware>();

// Swagger is on in every environment so the deployed demo can be explored.
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
