import { context, trace } from '@opentelemetry/api';
import { http, HttpResponse } from 'msw';

import { server } from '@/test/server';
import { failWith, serveAntiforgeryToken, serveTransactions } from '@/test/support';

import {
  getApiV1BffSession,
  getApiV1Transactions,
  postApiV1BffLogout,
} from './generated/disputedesk';
import { ApiError, onApiResponse } from './mutator';

// Criterion 1: every API call goes through the one mutator.
describe('the Orval mutator', () => {
  it('sends same-origin credentials and no antiforgery header on a safe method', async () => {
    let antiforgeryHeader: string | null = 'not read';
    server.use(
      http.get('*/api/v1/bff/session', ({ request }) => {
        antiforgeryHeader = request.headers.get('X-XSRF-TOKEN');
        return HttpResponse.json({ state: 'anonymous' });
      }),
    );
    const fetchSpy = vi.spyOn(globalThis, 'fetch');

    await getApiV1BffSession();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/v1/bff/session',
      expect.objectContaining({ credentials: 'same-origin' }),
    );
    expect(antiforgeryHeader).toBeNull();
  });

  it('fetches a token from the antiforgery endpoint and sends it as X-XSRF-TOKEN on an unsafe method', async () => {
    const requests: string[] = [];
    let tokenHeader: string | null = null;
    serveAntiforgeryToken('token-from-the-server');
    server.use(
      http.post('*/api/v1/bff/logout', ({ request }) => {
        tokenHeader = request.headers.get('X-XSRF-TOKEN');
        return new HttpResponse(null, { status: 200 });
      }),
    );
    server.events.on('request:start', ({ request }) => {
      requests.push(`${request.method} ${new URL(request.url).pathname}`);
    });
    const fetchSpy = vi.spyOn(globalThis, 'fetch');

    await postApiV1BffLogout();

    expect(requests).toEqual(['GET /api/v1/bff/antiforgery', 'POST /api/v1/bff/logout']);
    expect(tokenHeader).toBe('token-from-the-server');
    for (const [, init] of fetchSpy.mock.calls) {
      expect(init).toEqual(expect.objectContaining({ credentials: 'same-origin' }));
    }
  });

  it('does not send the unsafe request when the antiforgery token cannot be fetched', async () => {
    failWith('/api/v1/bff/antiforgery', 'unauthorized', 401);
    let logoutCalls = 0;
    server.use(
      http.post('*/api/v1/bff/logout', () => {
        logoutCalls += 1;
        return new HttpResponse(null, { status: 200 });
      }),
    );

    await expect(postApiV1BffLogout()).rejects.toMatchObject({ status: 401 });
    expect(logoutCalls).toBe(0);
  });

  it('throws an ApiError carrying the status and the problem body for a non-2xx response', async () => {
    failWith('/api/v1/transactions', 'core-banking-unavailable', 503);

    const error = await getApiV1Transactions().catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 503, problem: { traceId: 'trace-1' } });
  });

  it('throws an ApiError with no problem when the error body is not a problem', async () => {
    server.use(
      http.get(
        '*/api/v1/transactions',
        () => new HttpResponse('<html>Bad gateway</html>', { status: 502 }),
      ),
    );

    const error = await getApiV1Transactions().catch((caught: unknown) => caught);

    expect(error).toMatchObject({ status: 502, problem: undefined });
  });

  it('keeps the Retry-After header of an error response, raw, and null when there is none', async () => {
    server.use(
      http.get(
        '*/api/v1/transactions',
        () => new HttpResponse(null, { status: 429, headers: { 'Retry-After': '900' } }),
      ),
    );
    const withHeader = await getApiV1Transactions().catch((caught: unknown) => caught);
    failWith('/api/v1/transactions', 'core-banking-unavailable', 503);
    const without = await getApiV1Transactions().catch((caught: unknown) => caught);

    expect(withHeader).toMatchObject({ status: 429, retryAfter: '900' });
    expect(without).toMatchObject({ status: 503, retryAfter: null });
  });

  it('tells listeners the status of every response, success or failure', async () => {
    const statuses: number[] = [];
    const stopListening = onApiResponse((status) => statuses.push(status));
    serveTransactions();
    await getApiV1Transactions();
    failWith('/api/v1/transactions', 'core-banking-unavailable', 503);
    await getApiV1Transactions().catch(() => undefined);
    stopListening();
    await getApiV1Transactions().catch(() => undefined);

    expect(statuses).toEqual([200, 503]);
  });

  // Criterion 2: the context crosses the await between the antiforgery fetch and the request.
  it('makes the antiforgery fetch and the request children of the span active at the call', async () => {
    const traceparents: Record<string, string | null> = {};
    serveAntiforgeryToken();
    server.events.on('request:start', ({ request }) => {
      traceparents[`${request.method} ${new URL(request.url).pathname}`] =
        request.headers.get('traceparent');
    });
    server.use(http.post('*/api/v1/bff/logout', () => new HttpResponse(null, { status: 200 })));
    const span = trace.getTracer('test').startSpan('caller');

    await context.with(trace.setSpan(context.active(), span), () => postApiV1BffLogout());

    const traceId = span.spanContext().traceId;
    expect(traceparents['GET /api/v1/bff/antiforgery']).toContain(`-${traceId}-`);
    expect(traceparents['POST /api/v1/bff/logout']).toContain(`-${traceId}-`);
  });
});
