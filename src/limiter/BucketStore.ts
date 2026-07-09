import { TokenBucket } from './TokenBucket';

export interface BucketStoreOptions {
  capacity: number;
  refillRate: number;
}

/**
 * Holds one TokenBucket per client (keyed by API key).
 *
 * Single responsibility: create / retrieve / delete buckets. It contains no
 * rate-limiting logic itself — that lives in the bucket. In production this
 * in-memory Map would be swapped for something like Redis so multiple servers
 * share the same state.
 */
export class BucketStore {
  private readonly buckets = new Map<string, TokenBucket>();
  private readonly options: BucketStoreOptions;

  constructor(options: BucketStoreOptions) {
    this.options = options;
  }

  /**
   * Get the bucket for a key, lazily creating a fresh (full) one on first use.
   */
  getBucket(key: string): TokenBucket {
    let bucket = this.buckets.get(key);
    if (!bucket) {
      bucket = new TokenBucket({
        capacity: this.options.capacity,
        refillRate: this.options.refillRate,
      });
      this.buckets.set(key, bucket);
    }
    return bucket;
  }

  /** Remove a client's bucket (optional; e.g. for cleanup/tests). */
  deleteBucket(key: string): boolean {
    return this.buckets.delete(key);
  }

  /** Number of tracked clients — handy for stats/tests. */
  size(): number {
    return this.buckets.size;
  }
}
