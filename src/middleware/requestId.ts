import { Response, NextFunction } from 'express';
import { v4 as uuidv4 } from 'uuid';
import { AppRequest } from '../models/RequestMetadata';

/**
 * Ensures every request has a request id.
 *
 * Uses an incoming `x-request-id` header if the client sent one, otherwise
 * generates a UUID. The id is attached to the request (for logging / error
 * bodies) and echoed back in the `X-Request-ID` response header.
 */
export function requestId(
  req: AppRequest,
  res: Response,
  next: NextFunction,
): void {
  const incoming = req.header('x-request-id');
  const id = incoming && incoming.trim() !== '' ? incoming : uuidv4();
  req.requestId = id;
  res.setHeader('X-Request-ID', id);
  next();
}
