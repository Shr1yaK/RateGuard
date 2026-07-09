// Runs before any app module is imported (see jest.config setupFiles).
// Use a very slow refill during HTTP integration tests: slow enough that no
// full token refills during a test loop (so the capacity boundary is exact),
// but non-zero so retryAfter stays a finite number. Time-based refill is
// covered deterministically in the TokenBucket unit tests via fake timers.
process.env.CAPACITY = '10';
process.env.REFILL_RATE = '0.5';
