import { context, propagation, trace } from '@opentelemetry/api';
import { act } from '@testing-library/react';
import type { Mock } from 'vitest';

import { server } from '@/test/server';
import { signInAs } from '@/test/support';

import { getApiV1BffSession } from '@/api/generated/disputedesk';

// A spy over the real `startTracing`: a build that wrongly starts tracing really registers it, and the "off" test fails.
vi.mock('@/telemetry/startTracing', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/telemetry/startTracing')>();
  return { startTracing: vi.fn(original.startTracing) };
});

// A spy over the real `createRoot`: `main.tsx` does not expose its React root, and each test must unmount it.
vi.mock('react-dom/client', async (importOriginal) => {
  const original = await importOriginal<typeof import('react-dom/client')>();
  return { ...original, createRoot: vi.fn(original.createRoot) };
});

/** The two spies of the latest `startApp`. `afterEach` uses them to stop what that app started. */
let startedApp:
  | {
      startTracing: Mock<typeof import('@/telemetry/startTracing').startTracing>;
      createRoot: Mock<typeof import('react-dom/client').createRoot>;
    }
  | undefined;

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
  const { createRoot } = await import('react-dom/client');
  await import('@/main');
  startedApp = { startTracing: vi.mocked(startTracing), createRoot: vi.mocked(createRoot) };
  return startedApp.startTracing;
}

// The app's React root and tracer provider outlive the test unless the test stops them. Their timers (React's scheduler,
// the span processor's export timer) then fire after the jsdom environment is gone ("window is not defined"), and the
// app's requests reach MSW after `server.resetHandlers()`. This hook runs before the afterEach in setup.ts.
afterEach(async () => {
  for (const result of startedApp?.createRoot.mock.results ?? []) {
    if (result.type !== 'return') continue;
    const root = result.value;
    act(() => {
      root.unmount();
    });
  }
  const shutdowns: Promise<void>[] = [];
  for (const result of startedApp?.startTracing.mock.results ?? []) {
    if (result.type === 'return') shutdowns.push(result.value.shutdown());
  }
  await Promise.all(shutdowns);
  startedApp = undefined;
  trace.disable();
  context.disable();
  propagation.disable();
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
