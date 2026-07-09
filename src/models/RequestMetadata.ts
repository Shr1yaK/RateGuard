import { Request } from 'express';

/**
 * Fields our middleware attaches to the Express request as it flows through
 * the chain (request id, authenticated API key).
 */
export interface RequestMetadata {
  requestId: string;
  apiKey?: string;
}

/** Express Request augmented with our per-request metadata. */
export type AppRequest = Request & Partial<RequestMetadata>;
