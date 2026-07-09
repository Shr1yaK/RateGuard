import { Response, NextFunction } from 'express';
import { AppRequest } from '../models/RequestMetadata';

/**
 * Minimal API-key auth.
 *
 * Reads the `x-api-key` header. If it's missing, responds 401. Otherwise it
 * attaches the key to the request (the rate limiter uses it to pick a bucket)
 * and continues. There's no key allow-list here — any non-empty key is a
 * distinct identity, which is enough for per-client rate limiting.
 */
export function auth(
  req: AppRequest,
  res: Response,
  next: NextFunction,
): void {
  const apiKey = req.header('x-api-key');
  if (!apiKey || apiKey.trim() === '') {
    res.status(401).json({
      error: 'Missing API key',
      requestId: req.requestId,
    });
    return;
  }
  req.apiKey = apiKey;
  next();
}
