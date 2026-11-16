# RateGuard

A token-bucket API rate limiter built as Express middleware in TypeScript.

Each client (identified by API key) gets an independent token bucket. Requests
cost one token; tokens refill continuously over time. When a bucket runs dry the
request is rejected with `429 Too Many Requests`. This lets clients burst up to
capacity while capping their sustained rate — the same approach production APIs
use.

## C# / ASP.NET Core port

The [`dotnet/`](dotnet/) folder has a port of RateGuard to C# / ASP.NET Core
(.NET 10) using the same token-bucket algorithm, with xUnit unit and
integration tests. It is deployed on Azure App Service:
https://rateguard-dotnet-615n.azurewebsites.net/swagger. See
[dotnet/README.md](dotnet/README.md) for its design and limitations.

## Features

- **Token bucket algorithm** — continuous, lazy refill (no background timers)
- **Middleware architecture** — rate limiting is middleware, not endpoint logic
- **API key isolation** — one independent bucket per client
- **Configurable limits** — capacity and refill rate via environment variables
- **Standard headers** — `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`
- **Request IDs** — every response carries an `X-Request-ID` (echoed or generated)
- **Swappable strategy** — everything sits behind an `IRateLimiter` interface
- **Tested** — unit tests for the bucket, integration tests for the HTTP layer

## Architecture

```
Client
  |
  |  HTTP request
  v
Express
  |
  v
requestId middleware      attach / echo X-Request-ID
  |
  v
auth middleware           x-api-key present?  --no-->  401
  |
  v
rate limiter middleware   bucket.consume()?
  |                            |
  |  yes                       |  no
  v                            v
route handler              429 Too Many Requests
  |
  v
response (+ X-RateLimit-* headers)
```

The rate limiter is a middleware in the chain, so every protected route is
guarded the same way without touching the handler.

## Getting started

```bash
npm install

# optional: copy the example env (defaults are fine without it)
cp .env.example .env

# run in watch mode
npm run dev
```

The server logs:

```
RateGuard listening on http://localhost:3000 (capacity=10, refillRate=2/s)
```

## Usage

```bash
# missing key -> 401
curl -i http://localhost:3000/hello

# with a key -> 200 + rate-limit headers
curl -i -H "x-api-key: demo-key" http://localhost:3000/hello

# hammer it: first 10 succeed, then 429
for i in $(seq 1 12); do
  curl -s -o /dev/null -w "%{http_code}\n" -H "x-api-key: demo-key" http://localhost:3000/hello
done
```

### `GET /hello`

Protected by auth + rate limiting. Returns `{ "message": "Hello World", ... }`.

**Success headers**

| Header                  | Meaning                                     |
| ----------------------- | ------------------------------------------- |
| `X-RateLimit-Limit`     | Bucket capacity                             |
| `X-RateLimit-Remaining` | Tokens left                                 |
| `X-RateLimit-Reset`     | Unix time when the next token is available  |
| `X-Request-ID`          | Request identifier (echoed or generated)    |

**Rate-limited response** (`429`)

```json
{
  "error": "Rate limit exceeded",
  "requestId": "…",
  "retryAfter": 2,
  "remainingTokens": 0
}
```

## Configuration

Set via environment variables (see `.env.example`):

| Variable      | Default | Description                       |
| ------------- | ------- | --------------------------------- |
| `PORT`        | `3000`  | Server port                       |
| `CAPACITY`    | `10`    | Max tokens per bucket (burst size)|
| `REFILL_RATE` | `2`     | Tokens refilled per second        |

## Testing

```bash
npm test
```

Unit tests cover the token bucket (consume, block when empty, time-based refill
via fake timers, capacity cap). Integration tests (Supertest) cover auth, the
rate-limit headers, the 10-then-429 boundary, per-key isolation, and request IDs.

## Project structure

```
src/
  app.ts            Express app + middleware wiring
  server.ts         entry point
  config.ts         env-driven configuration
  limiter/
    IRateLimiter.ts strategy interface
    TokenBucket.ts  token bucket implementation
    BucketStore.ts  one bucket per api key
  middleware/
    requestId.ts    attach / echo X-Request-ID
    auth.ts         api key check
    rateLimiter.ts  consume a token or return 429
  routes/
    api.ts          GET /hello
  models/
    RequestMetadata.ts
  utils/
    time.ts
tests/
dotnet/             C# / ASP.NET Core port (see dotnet/README.md)
```

## Tech stack

TypeScript, Node.js, Express, Jest + Supertest, ESLint, Prettier, uuid.
