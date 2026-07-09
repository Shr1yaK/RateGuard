import { Response, NextFunction } from 'express';
import { AppRequest } from '../models/RequestMetadata';
import { BucketStore } from '../limiter/BucketStore';

/**
 * Builds the rate-limiting middleware around a given bucket store.
 *
 * For each request it looks up the caller's bucket (by API key) and tries to
 * consume a token. On success it sets the standard X-RateLimit-* headers and
 * calls next(). On failure it responds 429 with a JSON body.
 *
 * `X-RateLimit-Reset` is computed dynamically from the bucket's retryAfter —
 * no fixed window needed.
 */
export function createRateLimiter(store: BucketStore) {
  return function rateLimiter(
    req: AppRequest,
    res: Response,
    next: NextFunction,
  ): void {
    // auth middleware guarantees apiKey is set before this runs.
    const apiKey = req.apiKey as string;
    const bucket = store.getBucket(apiKey);

    const allowed = bucket.consume();
    const remaining = bucket.availableTokens();
    const retryAfter = bucket.retryAfter();
    const resetEpoch = Math.ceil(Date.now() / 1000 + retryAfter);

    res.setHeader('X-RateLimit-Limit', bucket.getCapacity());
    res.setHeader('X-RateLimit-Remaining', remaining);
    res.setHeader('X-RateLimit-Reset', resetEpoch);

    if (allowed) {
      next();
      return;
    }

    const retryAfterSeconds = Math.ceil(retryAfter);
    res.setHeader('Retry-After', retryAfterSeconds);
    res.status(429).json({
      error: 'Rate limit exceeded',
      requestId: req.requestId,
      retryAfter: retryAfterSeconds,
      remainingTokens: remaining,
    });
  };
}
