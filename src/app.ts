import express, { Application } from 'express';
import { config } from './config';
import { BucketStore } from './limiter/BucketStore';
import { requestId } from './middleware/requestId';
import { auth } from './middleware/auth';
import { createRateLimiter } from './middleware/rateLimiter';
import { apiRouter } from './routes/api';

/**
 * Builds the Express app and wires the middleware chain:
 *   requestId -> auth -> rateLimiter -> routes
 *
 * The rate limiter is middleware, not part of any endpoint — every protected
 * route is guarded the same way.
 */
export function createApp(): Application {
  const app = express();
  app.use(express.json());

  const store = new BucketStore({
    capacity: config.capacity,
    refillRate: config.refillRate,
  });

  app.use(requestId);
  app.use(auth);
  app.use(createRateLimiter(store));

  app.use(apiRouter);

  return app;
}
