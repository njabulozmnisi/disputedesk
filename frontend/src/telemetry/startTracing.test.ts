import { context, trace } from '@opentelemetry/api';
import { http, HttpResponse } from 'msw';

import { server } from '@/test/server';
import { endedSpans, flushSpans, spansNamed } from '@/test/spans';
import { signInAs } from '@/test/support';

import { getApiV1BffSession } from '@/api/generated/disputedesk';

const w3cTraceparent = /^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$/;

/** The request headers of the first request MSW sees for `url`. */
function captureHeaders(method: 'get' | 'post', url: string) {
  const seen: Headers[] = [];
  server.use(
    http[method](url, ({ request }) => {
      seen.push(request.headers);
      return HttpResponse.json({ state: 'anonymous' });
    }),
  );
  return seen;
}

// Criterion 1: tracing starts once; traceparent goes only on same-origin requests.
describe('tracing start-up', () => {
  it('adds a W3C traceparent and no baggage to a same-origin /api fetch', async () => {
    const seen = captureHeaders('get', '*/api/v1/bff/session');

    await getApiV1BffSession();

    expect(seen[0]?.get('traceparent')).toMatch(w3cTraceparent);
    expect(seen[0]?.has('baggage')).toBe(false);
  });

  it('adds neither traceparent nor baggage to a fetch to another origin', async () => {
    const seen = captureHeaders('get', 'https://other.example/x');

    await fetch('https://other.example/x');

    expect(seen[0]?.has('traceparent')).toBe(false);
    expect(seen[0]?.has('baggage')).toBe(false);
  });

  it('names the service disputedesk-web and carries the SDK attributes, nothing from the browser', async () => {
    signInAs('anonymous');
    await getApiV1BffSession();

    const keys = Object.keys(endedSpans()[0]?.resource.attributes ?? {}).sort();

    expect(endedSpans()[0]?.resource.attributes['service.name']).toBe('disputedesk-web');
    expect(keys).toEqual([
      'service.name',
      'telemetry.sdk.language',
      'telemetry.sdk.name',
      'telemetry.sdk.version',
    ]);
  });

  it('registers the stack context manager: the active span is the parent of a started span', () => {
    const tracer = trace.getTracer('test');
    const parent = tracer.startSpan('parent');

    const child = context.with(trace.setSpan(context.active(), parent), () =>
      tracer.startSpan('child'),
    );

    expect(child.spanContext().traceId).toBe(parent.spanContext().traceId);
  });
});

// Criterion 6: one OTLP/HTTP JSON POST to the same origin; the export itself is never traced.
describe('the export', () => {
  it('posts the spans once to /otlp/v1/traces on the same origin as JSON, untraced', async () => {
    signInAs('anonymous');
    await getApiV1BffSession();
    await flushSpans();
    const exports: Request[] = [];
    server.use(
      http.post('*/otlp/v1/traces', ({ request }) => {
        exports.push(request.clone());
        return HttpResponse.json({});
      }),
    );
    await getApiV1BffSession();

    await flushSpans();

    expect(exports).toHaveLength(1);
    expect(new URL(exports[0]?.url ?? '').origin).toBe(window.location.origin);
    expect(new URL(exports[0]?.url ?? '').pathname).toBe('/otlp/v1/traces');
    expect(exports[0]?.headers.get('content-type')).toBe('application/json');
    expect(exports[0]?.headers.has('traceparent')).toBe(false);
    expect(exports[0]?.headers.has('authorization')).toBe(false);
    expect(spansNamed('POST')).toHaveLength(0);
  });
});
