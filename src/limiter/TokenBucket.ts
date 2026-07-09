import { IRateLimiter } from './IRateLimiter';
import { nowSeconds } from '../utils/time';

export interface TokenBucketOptions {
  /** Maximum tokens the bucket can hold (burst size). */
  capacity: number;
  /** Tokens added per second. */
  refillRate: number;
}

/**
 * Token bucket rate limiter.
 *
 * A bucket holds up to `capacity` tokens. Each request consumes one token.
 * Tokens refill continuously at `refillRate` per second, computed lazily from
 * the elapsed time since the last refill — no background timer needed.
 *
 * This lets a client burst up to `capacity` requests, while capping their
 * sustained rate at `refillRate` requests/second.
 */
export class TokenBucket implements IRateLimiter {
  private readonly capacity: number;
  private readonly refillRate: number;
  private tokens: number;
  private lastRefillTimestamp: number;

  constructor(options: TokenBucketOptions) {
    this.capacity = options.capacity;
    this.refillRate = options.refillRate;
    this.tokens = options.capacity; // start full
    this.lastRefillTimestamp = nowSeconds();
  }

  /**
   * Add tokens for the time elapsed since the last refill, capped at capacity.
   */
  private refill(): void {
    const now = nowSeconds();
    const elapsed = now - this.lastRefillTimestamp;
    if (elapsed <= 0) {
      return;
    }
    this.tokens = Math.min(this.capacity, this.tokens + elapsed * this.refillRate);
    this.lastRefillTimestamp = now;
  }

  /**
   * Try to consume one token. Refills first, then spends a token if available.
   */
  consume(): boolean {
    this.refill();
    if (this.tokens >= 1) {
      this.tokens -= 1;
      return true;
    }
    return false;
  }

  /** Whole tokens currently available (after refill). */
  availableTokens(): number {
    this.refill();
    return Math.floor(this.tokens);
  }

  /**
   * Seconds until the next whole token is available. 0 if one is available now.
   */
  retryAfter(): number {
    this.refill();
    if (this.tokens >= 1) {
      return 0;
    }
    const missing = 1 - this.tokens;
    return missing / this.refillRate;
  }

  /** Max tokens the bucket can hold — exposed for the X-RateLimit-Limit header. */
  getCapacity(): number {
    return this.capacity;
  }
}
