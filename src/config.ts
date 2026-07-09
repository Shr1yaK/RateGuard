import dotenv from 'dotenv';

dotenv.config();

/**
 * Reads a numeric environment variable, falling back to a default when it is
 * unset or not a valid number.
 */
function numberFromEnv(key: string, fallback: number): number {
  const raw = process.env[key];
  if (raw === undefined || raw.trim() === '') {
    return fallback;
  }
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : fallback;
}

export const config = {
  port: numberFromEnv('PORT', 3000),
  /** Maximum tokens a bucket can hold — also the burst size. */
  capacity: numberFromEnv('CAPACITY', 10),
  /** Tokens refilled per second. */
  refillRate: numberFromEnv('REFILL_RATE', 2),
};

export type Config = typeof config;
