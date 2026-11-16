# RateGuard (.NET)

A per-client API rate limiter for ASP.NET Core, built on a hand-written
**token bucket**. It is a C# port of the [TypeScript/Express version](../README.md)
at the root of this repo, and uses the same algorithm.

- .NET 10 (LTS), ASP.NET Core Web API with controllers
- No rate-limiting libraries (`Microsoft.AspNetCore.RateLimiting` is not used)
- Custom middleware, an in-memory bucket store, config-driven policies
- xUnit unit tests (with a fake clock) and integration tests (with `WebApplicationFactory`)

> **Scope:** single node, in memory. See [Limitations](#limitations).

## How the token bucket works

Each client has a bucket that holds up to `Capacity` tokens and starts full.
Every request costs one token. Tokens refill continuously at
`RefillRatePerSecond`. When the bucket is empty, the request gets `429 Too Many Requests`.

So a client can **burst** up to `Capacity` requests, and its **sustained** rate
is capped at `RefillRatePerSecond`. With the defaults (10 tokens, 2/sec), a
client can send 10 requests at once, then about 2 per second.

On every request, under the bucket's lock:

```
elapsed = now - lastRefill
tokens  = min(capacity, tokens + elapsed * refillRate)
lastRefill = now
if tokens >= 1: tokens -= 1, allow
else:           reject, retryAfter = (1 - tokens) / refillRate
```

## Architecture

```
 HTTP request
      │
      ▼
┌──────────────────────────────┐   /health, /swagger ──► skipped (never limited)
│   RateLimitingMiddleware     │
│  1. clientId = X-Api-Key     │
│     header, else remote IP   │
│  2. bucket = store           │──────────┐
│       .GetOrCreateBucket()   │          │
│  3. bucket.TryConsume()      │          ▼
└──────────────┬───────────────┘   ┌──────────────────────────────┐
               │                   │ IRateLimitStore  (singleton) │
     allowed?  │                   │  InMemoryRateLimitStore      │
   ┌───────────┴──────────┐        │  ConcurrentDictionary<       │
   │ yes                  │ no     │    clientId, TokenBucket>    │
   ▼                      ▼        │  policies from appsettings + │
 X-RateLimit-Limit     429 +       │  PUT /api/limits overrides   │
 X-RateLimit-Remaining Retry-After └──────────────┬───────────────┘
   │                   problem+json               │ one per client
   ▼                                              ▼
┌──────────────────────────────┐   ┌──────────────────────────────┐
│ Controllers                  │   │ TokenBucket                  │
│  HealthController   /health  │   │  capacity, refill rate       │
│  ResourceController          │   │  lazy refill, lock per bucket│
│     /api/resource            │   │  TryConsume(), GetSnapshot() │
│  LimitsController            │   │  time from TimeProvider      │
│     /api/limits/{clientId}   │   └──────────────────────────────┘
└──────────────────────────────┘
```

```
src/RateGuard.Api/
  Program.cs                          DI registration and middleware pipeline
  RateLimiting/
    TokenBucket.cs                    the algorithm (+ BucketSnapshot)
    IRateLimitStore.cs                store abstraction
    InMemoryRateLimitStore.cs         ConcurrentDictionary-backed store
    RateLimitPolicy.cs                Capacity + RefillRatePerSecond, with [Range] validation
    RateLimitOptions.cs               bound from the "RateLimiting" config section
    RateLimitingMiddleware.cs         identifies client, consumes token, 429s
  Controllers/                        Health, Resource, Limits
  Models/LimitStatusResponse.cs       response DTO for /api/limits
tests/RateGuard.Tests/
  TokenBucketTests.cs                 unit tests with FakeTimeProvider
  InMemoryRateLimitStoreTests.cs      unit tests for the store and policy validation
  RateGuardFactory.cs                 WebApplicationFactory with fake clock and small limits
  RateLimitingIntegrationTests.cs     end-to-end HTTP tests
```

## Design decisions

**Why lazy refill?** A bucket is only refilled when it is used: the elapsed
time since the last refill is turned into tokens. There is no background timer
that has to wake up and top up every bucket. That means no timer threads, no
work for idle clients, and the cost of a request is O(1) regardless of how
many clients exist. Tokens are stored as a `double` so partial refills (0.5
tokens after 250 ms at 2/sec) are not lost.

**Why a lock per bucket?** `TryConsume` is a read-modify-write: refill, check
`tokens >= 1`, subtract. Without a lock, two threads can both see the last
token and both spend it. A unit test fires 5,000 parallel requests at a
1,000-token bucket and asserts exactly 1,000 succeed. With the lock removed it
issued 1,009-1,013. The lock is **per bucket**, not global, so clients never
wait on each other; only concurrent requests from the same client contend.
The lock is held only for a few arithmetic operations, never across I/O.

**Why a singleton store?** All requests must see the same buckets. A scoped or
transient store would create a new, full bucket per request and nothing would
ever be limited. The middleware is also created once per app, so it can only
take singleton dependencies through its constructor. Thread safety for the
map comes from `ConcurrentDictionary`; thread safety for each bucket's token
count comes from that bucket's lock.

**Why `TimeProvider` instead of `DateTime.Now`?** The clock is injected, so
tests use `FakeTimeProvider` and move time forward explicitly: no `Thread.Sleep`,
no flaky timing. The bucket uses `GetTimestamp()`, a monotonic clock, so a
system clock change cannot create or destroy tokens.

**Why an interface for the store?** The middleware and controllers depend on
`IRateLimitStore`, so a distributed implementation (e.g. Redis) could replace
the in-memory one without changing them.

**Other choices**
- The decision and the header values come from one `TryConsume(out snapshot)`
  call under one lock, so headers always match the decision.
- `Retry-After` is rounded **up** to whole seconds, so it is never `0` on a 429.
- Errors use RFC 7807 `application/problem+json`, the ASP.NET Core standard.
- Invalid config (e.g. `Capacity: 0`) stops the app at startup (`ValidateOnStart`).
- `PUT` replaces the client's bucket with a fresh, full one under the new policy.

## API

| Method | Path | Limited | Description |
|---|---|---|---|
| GET | `/health` | no | `{"status":"healthy"}` |
| GET | `/api/resource` | yes | sample protected resource |
| GET | `/api/limits/{clientId}` | yes | current tokens and capacity; 404 if the client was never seen |
| PUT | `/api/limits/{clientId}` | yes | set a custom policy; 400 if invalid |
| GET | `/swagger` | no | Swagger UI (enabled in all environments) |

Limited responses carry `X-RateLimit-Limit` and `X-RateLimit-Remaining`.
A rejected request returns `429` with `Retry-After` (seconds) and a problem+json body.

The client is identified by the `X-Api-Key` header. Without it, the caller's IP
address is used.

## Configuration

`src/RateGuard.Api/appsettings.json`:

```json
"RateLimiting": {
  "Default": { "Capacity": 10, "RefillRatePerSecond": 2 },
  "Clients": {
    "demo-premium-key": { "Capacity": 50, "RefillRatePerSecond": 10 }
  }
}
```

Any value can be overridden with an environment variable, using `__` for nesting,
e.g. `RateLimiting__Default__Capacity=20`. Valid ranges: `Capacity` 1 to 1,000,000,
`RefillRatePerSecond` 0.01 to 1,000,000.

## Run and test

Requires the .NET 10 SDK. Run every command in this README from the `dotnet/` folder.

```bash
dotnet run --project src/RateGuard.Api
```

The app listens on `http://localhost:5054`; Swagger UI is at `http://localhost:5054/swagger`.

```bash
dotnet test
```

## curl examples

```bash
BASE=http://localhost:5054
```

Health check (never limited):

```bash
curl -i $BASE/health
```

One request as client `alice`; note the `X-RateLimit-*` headers:

```bash
curl -i -H "X-Api-Key: alice" $BASE/api/resource
```

Burst 12 requests: the first 10 succeed, then 429:

```bash
for i in $(seq 1 12); do curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: alice" $BASE/api/resource; done; echo
```

See a 429 in full (`Retry-After` header and problem+json body):

```bash
curl -i -H "X-Api-Key: alice" $BASE/api/resource
```

Check alice's bucket:

```bash
curl -s -H "X-Api-Key: admin" $BASE/api/limits/alice
```

Give alice a custom policy (3 tokens, 1 every 2 seconds):

```bash
curl -s -X PUT -H "X-Api-Key: admin" -H "Content-Type: application/json" -d '{"capacity":3,"refillRatePerSecond":0.5}' $BASE/api/limits/alice
```

Invalid policy returns 400 with field errors:

```bash
curl -s -X PUT -H "X-Api-Key: admin" -H "Content-Type: application/json" -d '{"capacity":0,"refillRatePerSecond":2}' $BASE/api/limits/alice
```

## Limitations

This is deliberately a **single-node, in-memory** limiter:

- **State lives in process memory and resets on restart.** Every bucket and
  every policy set with `PUT` is lost when the app restarts, redeploys, or (on
  Azure's Free tier) is unloaded after being idle.
- **Not distributed.** If the app runs on N instances (scale-out), each has its
  own buckets, so a client can get up to N times its limit. Fixing this needs
  shared state, e.g. Redis with an atomic Lua script per request.
- **No eviction.** One bucket is kept per client ever seen, so memory grows with
  the number of distinct API keys and IPs. A real system would evict idle buckets.
- **No authentication.** Any non-empty `X-Api-Key` is accepted as an identity, so
  a client can switch keys to get a fresh bucket, and `PUT /api/limits` can be
  called by anyone. A real system would validate keys and restrict `PUT` to admins.
- **IP fallback is coarse.** Users behind the same NAT or proxy share one bucket.

## Deployment

**Live demo:** https://rateguard-dotnet-615n.azurewebsites.net
([Swagger UI](https://rateguard-dotnet-615n.azurewebsites.net/swagger))

Hosted on Azure App Service, Linux, Free (F1) tier, region Central India.

```bash
B=https://rateguard-dotnet-615n.azurewebsites.net
curl -s $B/health
```

Over the internet each request takes a round trip, so sending requests one
after another arrives at roughly the refill rate and is rarely limited. To see
429s, send a burst **in parallel**:

```bash
seq 1 20 | xargs -P 20 -I{} curl -s -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: burst-test" $B/api/resource | sort | uniq -c
```

Expected: about 10-11 `200` and the rest `429` (10 tokens, plus whatever refills
while the burst is in flight).

### How it was deployed

From the `dotnet/` folder:

```bash
# one-time: enable App Service on a new subscription
az provider register --namespace Microsoft.Web --wait

# resources: a group, a free Linux plan, and the web app on .NET 10
az group create --name rateguard-rg --location centralindia
az appservice plan create --name rateguard-plan --resource-group rateguard-rg --location centralindia --sku F1 --is-linux
az webapp create --name rateguard-dotnet-615n --resource-group rateguard-rg --plan rateguard-plan --runtime "DOTNETCORE:10.0"
az webapp config set --name rateguard-dotnet-615n --resource-group rateguard-rg --startup-file "dotnet RateGuard.Api.dll"
az webapp update --name rateguard-dotnet-615n --resource-group rateguard-rg --https-only true

# build locally, upload the package
dotnet publish src/RateGuard.Api -c Release -o publish
(cd publish && zip -r ../app.zip .)
az webapp deploy --name rateguard-dotnet-615n --resource-group rateguard-rg --src-path app.zip --type zip
```

**Free tier caveats:** F1 has no "Always On", so the app is unloaded after a
period of inactivity. The next request cold-starts it (a few seconds) and, since
state is in memory, all buckets and `PUT` policies start fresh. F1 also has a
daily CPU quota.

### Deleting everything

Everything lives in one resource group, so one command removes the app, the
plan, and the group:

```bash
az group delete --name rateguard-rg --yes --no-wait
```
