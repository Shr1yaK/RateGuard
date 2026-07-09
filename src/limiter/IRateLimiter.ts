/**
 * Common contract for any rate-limiting strategy.
 *
 * Today we only ship a token bucket, but coding against an interface means a
 * Sliding Window / Leaky Bucket / Fixed Window implementation can be dropped in
 * later without touching the middleware.
 */
export interface IRateLimiter {
  /**
   * Attempt to consume a single token for one request.
   * @returns true if the request is allowed, false if it should be blocked.
   */
  consume(): boolean;

  /** Tokens currently available (after accounting for refill). */
  availableTokens(): number;

  /**
   * Seconds the caller should wait before a token becomes available.
   * Returns 0 when a token is available right now.
   */
  retryAfter(): number;
}
