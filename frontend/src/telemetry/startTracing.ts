import { context, propagation, trace } from '@opentelemetry/api';
import { W3CTraceContextPropagator } from '@opentelemetry/core';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
// shortcut: experimental package pinned at 0.9.0, a minor release may break its API; re-check the fetch options and span attributes on every upgrade, and move to a stable export when one ships
import { FetchInstrumentation } from '@opentelemetry/browser-instrumentation/experimental/fetch';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { defaultResource, resourceFromAttributes } from '@opentelemetry/resources';
import {
  BatchSpanProcessor,
  StackContextManager,
  TracerProvider,
  type SpanProcessor,
} from '@opentelemetry/sdk-trace';

import { sanitizeUrl } from './sanitizeUrl';

/**
 * Starts browser tracing (AD-20): same-origin `/api` fetches carry `traceparent`, and the spans are exported as OTLP/HTTP
 * JSON to this origin's `/otlp/v1/traces`, which the gateway proxies. `extraSpanProcessors` is for tests.
 */
export function startTracing(extraSpanProcessors: SpanProcessor[] = []): TracerProvider {
  // The exporter's transport parses its URL with no base, so the URL must be absolute.
  const exporter = new OTLPTraceExporter({
    url: new URL('/otlp/v1/traces', window.location.origin).href,
  });
  // No browser resource detector: no user agent or language goes into the telemetry.
  const resource = defaultResource().merge(
    resourceFromAttributes({ 'service.name': 'disputedesk-web' }),
  );
  const provider = new TracerProvider({
    resource,
    spanProcessors: [new BatchSpanProcessor({ exporter }), ...extraSpanProcessors],
  });

  trace.setGlobalTracerProvider(provider);
  context.setGlobalContextManager(new StackContextManager());
  // `traceparent` only: no baggage propagator, so no `baggage` header is ever sent.
  propagation.setGlobalPropagator(new W3CTraceContextPropagator());
  // Only the fetch instrumentation: the others record page URLs, element text, console output or errors. It injects
  // `traceparent` only into requests to the page's own origin.
  registerInstrumentations({
    instrumentations: [new FetchInstrumentation({ measureRequestSize: false, sanitizeUrl })],
  });
  return provider;
}
