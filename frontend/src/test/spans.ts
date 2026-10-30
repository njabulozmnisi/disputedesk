import {
  InMemorySpanExporter,
  SimpleSpanProcessor,
  type ReadableSpan,
  type TracerProvider,
} from '@opentelemetry/sdk-trace';

import { startTracing } from '@/telemetry/startTracing';

/** Every span the app ends during a test; `setup.ts` empties it after each test. */
export const spanExporter = new InMemorySpanExporter();

let provider: TracerProvider | undefined;

/**
 * The real tracing set-up, plus a processor that keeps the ended spans in `spanExporter`. It runs after MSW starts, so
 * the fetch instrumentation wraps the fetch that MSW has already patched.
 */
export function startTestTracing() {
  provider = startTracing([new SimpleSpanProcessor({ exporter: spanExporter })]);
}

/** Sends the batched spans to the exporter now, as the batch timer would. */
export async function flushSpans() {
  await provider?.forceFlush();
}

export const endedSpans = (): ReadableSpan[] => spanExporter.getFinishedSpans();

export const spansNamed = (name: string) => endedSpans().filter((span) => span.name === name);
