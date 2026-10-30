import { context, propagation, trace } from '@opentelemetry/api';

import { server } from '@/test/server';
import { signInAs } from '@/test/support';

import { getApiV1BffSession } from '@/api/generated/disputedesk';

// A spy over the real `startTracing`: a build that wrongly starts tracing really registers it, and the "off" test fails.
vi.mock('@/telemetry/startTracing', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/telemetry/startTracing')>();
  return { startTracing: vi.fn(original.startTracing) };
});

/**
 * Runs `main.tsx` afresh with `VITE_BROWSER_TELEMETRY` set to `value` (unset when undefined). The tracing that the shared
 * test set-up (setup.ts) started is undone first, so only this app start can register it.
 */
async function startApp(value: string | undefined) {
  trace.disable();
  context.disable();
  propagation.disable();
  vi.stubEnv('VITE_BROWSER_TELEMETRY', value);
  vi.resetModules();
  document.body.innerHTML = '<div id="root"></div>';
  const { startTracing } = await import('@/telemetry/startTracing');
  await import('@/main');
  return vi.mocked(startTracing);
}

afterEach(() => {
  vi.unstubAllEnvs();
  document.body.innerHTML = '';
});

// Criterion 1: built with VITE_BROWSER_TELEMETRY=off, the app starts no tracing, exports nothing, sends no traceparent.
describe('VITE_BROWSER_TELEMETRY=off', () => {
  it('does not start tracing, posts nothing to /otlp/ and sends no traceparent on an /api request', async () => {
    const otlpRequests: string[] = [];
    const apiHeaders: Headers[] = [];
    server.events.on('request:start', ({ request }) => {
      const { pathname } = new URL(request.url);
      if (pathname.startsWith('/otlp/')) otlpRequests.push(request.url);
      if (pathname.startsWith('/api/')) apiHeaders.push(request.headers);
    });
    signInAs('anonymous');

    const startTracing = await startApp('off');
    await getApiV1BffSession();
    await vi.waitFor(() => {
      expect(apiHeaders.length).toBeGreaterThan(0);
    });
    // Only a build that wrongly started tracing has a provider to flush; this makes its export happen now, not on the timer.
    for (const result of startTracing.mock.results) {
      if (result.type === 'return') await result.value.forceFlush();
    }

    expect(startTracing).not.toHaveBeenCalled();
    expect(trace.getTracer('test').startSpan('probe').isRecording()).toBe(false);
    expect(otlpRequests).toEqual([]);
    for (const headers of apiHeaders) expect(headers.has('traceparent')).toBe(false);
  });
});

// Criterion 2: every other value leaves tracing on, started once. Only the exact value `off` turns it off.
describe('any other VITE_BROWSER_TELEMETRY value', () => {
  it.each([undefined, '', 'on', 'false', 'OFF', 'off '])(
    'starts tracing once when the value is %j',
    async (value) => {
      signInAs('anonymous');

      const startTracing = await startApp(value);

      expect(startTracing).toHaveBeenCalledTimes(1);
    },
  );
});
