/**
 * Current time in seconds (with sub-second precision).
 *
 * Kept behind a helper so tests can reason about time in seconds and, if
 * needed, mock a single source of "now".
 */
export function nowSeconds(): number {
  return Date.now() / 1000;
}
