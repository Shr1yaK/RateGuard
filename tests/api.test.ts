import request from 'supertest';
import { createApp } from '../src/app';

// Each test uses a unique api key so buckets don't leak state between tests
// (the app keeps one store for its lifetime). Refill is disabled in tests/setup.ts
// so the capacity boundary is exact.
const app = createApp();

describe('rate-limited API', () => {
  it('returns 401 when no api key is provided', async () => {
    const res = await request(app).get('/hello');
    expect(res.status).toBe(401);
    expect(res.body.error).toMatch(/api key/i);
  });

  it('returns 200 and rate-limit headers for an authorised request', async () => {
    const res = await request(app)
      .get('/hello')
      .set('x-api-key', 'headers-user');

    expect(res.status).toBe(200);
    expect(res.body.message).toBe('Hello World');
    expect(res.headers['x-ratelimit-limit']).toBe('10');
    expect(res.headers['x-ratelimit-remaining']).toBe('9');
    expect(res.headers['x-ratelimit-reset']).toBeDefined();
    expect(res.headers['x-request-id']).toBeDefined();
  });

  it('allows the first 10 requests then blocks the 11th with 429', async () => {
    const key = 'burst-user';
    const statuses: number[] = [];
    for (let i = 0; i < 11; i++) {
      const res = await request(app).get('/hello').set('x-api-key', key);
      statuses.push(res.status);
    }

    expect(statuses.slice(0, 10)).toEqual(Array(10).fill(200));
    expect(statuses[10]).toBe(429);
  });

  it('returns a structured 429 body when blocked', async () => {
    const key = 'body-user';
    for (let i = 0; i < 10; i++) {
      await request(app).get('/hello').set('x-api-key', key);
    }
    const res = await request(app).get('/hello').set('x-api-key', key);

    expect(res.status).toBe(429);
    expect(res.body).toMatchObject({
      error: 'Rate limit exceeded',
      remainingTokens: 0,
    });
    expect(res.body.requestId).toBeDefined();
    expect(typeof res.body.retryAfter).toBe('number');
  });

  it('keeps a separate bucket per api key', async () => {
    const heavy = 'iso-heavy';
    const fresh = 'iso-fresh';

    for (let i = 0; i < 10; i++) {
      await request(app).get('/hello').set('x-api-key', heavy);
    }
    const blocked = await request(app).get('/hello').set('x-api-key', heavy);
    expect(blocked.status).toBe(429);

    // A different key is unaffected.
    const ok = await request(app).get('/hello').set('x-api-key', fresh);
    expect(ok.status).toBe(200);
  });

  it('echoes a client-supplied x-request-id, otherwise generates one', async () => {
    const withId = await request(app)
      .get('/hello')
      .set('x-api-key', 'rid-user-1')
      .set('x-request-id', 'my-custom-id');
    expect(withId.headers['x-request-id']).toBe('my-custom-id');

    const generated = await request(app)
      .get('/hello')
      .set('x-api-key', 'rid-user-2');
    // uuid v4 shape
    expect(generated.headers['x-request-id']).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    );
  });
});
