import { TokenBucket } from '../src/limiter/TokenBucket';

describe('TokenBucket', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('starts full and consumes tokens one at a time', () => {
    const bucket = new TokenBucket({ capacity: 3, refillRate: 1 });
    expect(bucket.availableTokens()).toBe(3);

    expect(bucket.consume()).toBe(true);
    expect(bucket.availableTokens()).toBe(2);
  });

  it('blocks once the bucket is empty', () => {
    const bucket = new TokenBucket({ capacity: 2, refillRate: 1 });

    expect(bucket.consume()).toBe(true);
    expect(bucket.consume()).toBe(true);
    expect(bucket.consume()).toBe(false); // empty now
    expect(bucket.availableTokens()).toBe(0);
  });

  it('refills tokens over time at the refill rate', () => {
    const bucket = new TokenBucket({ capacity: 5, refillRate: 2 });
    for (let i = 0; i < 5; i++) bucket.consume();
    expect(bucket.availableTokens()).toBe(0);

    jest.advanceTimersByTime(1000); // 1 second -> +2 tokens
    expect(bucket.availableTokens()).toBe(2);
    expect(bucket.consume()).toBe(true);
  });

  it('never refills beyond capacity', () => {
    const bucket = new TokenBucket({ capacity: 3, refillRate: 10 });
    bucket.consume();

    jest.advanceTimersByTime(10_000); // way more than enough
    expect(bucket.availableTokens()).toBe(3); // capped at capacity
  });

  it('reports retryAfter as 0 when a token is available', () => {
    const bucket = new TokenBucket({ capacity: 1, refillRate: 2 });
    expect(bucket.retryAfter()).toBe(0);
  });

  it('reports the wait time when empty', () => {
    const bucket = new TokenBucket({ capacity: 1, refillRate: 2 });
    bucket.consume(); // empty
    expect(bucket.consume()).toBe(false);
    // need 1 token at 2 tokens/sec -> 0.5s
    expect(bucket.retryAfter()).toBeCloseTo(0.5, 5);
  });
});
