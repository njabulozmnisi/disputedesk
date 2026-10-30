import { context, trace, type Span, type SpanContext } from '@opentelemetry/api';

import { ApiError } from '@/api/mutator';
import { isProblem } from '@/api/problems';

// Spans for SM-1 (submitted ÷ opened) and SM-2 (open to 201), read from the traces (AD-20). They hold only a fixed
// outcome and an HTTP status: never a transaction ID, an account, a note or an amount.

const tracerName = 'disputedesk-web';

// The problem types a raise can end with; each slug is also the outcome's value.
const refusalOutcomes = [
  'already-disputed',
  'not-disputable',
  'not-found',
  'validation',
  'idempotency-in-progress',
  'idempotency-mismatch',
  'core-banking-unavailable',
] as const;

/** The only values `dispute.raise.outcome` takes: low-cardinality, as `error.type` is in the semantic conventions. */
export type RaiseOutcome =
  'submitted' | (typeof refusalOutcomes)[number] | 'unexpected' | 'network-error';

/** Records `dispute.raise.open` and ends it at once. Returns its context, for the submit spans to link to. */
export function recordRaiseOpen(): SpanContext {
  const span = trace.getTracer(tracerName).startSpan('dispute.raise.open');
  span.end();
  return span.spanContext();
}

/** Starts the span that wraps one submit attempt: the antiforgery fetch, the POST and every resend. */
export function startRaiseSubmit(open: SpanContext | undefined): Span {
  const links = open ? [{ context: open }] : [];
  return trace.getTracer(tracerName).startSpan('dispute.raise.submit', { links });
}

/** Runs `request` with `submit` as the active span, so every fetch it starts is a child of the submit span. */
export function withRaiseSubmit<T>(submit: Span, request: () => Promise<T>): Promise<T> {
  return context.with(trace.setSpan(context.active(), submit), request);
}

function outcomeOf(error: unknown): RaiseOutcome {
  if (error === null || error === undefined) return 'submitted';
  if (error instanceof TypeError) return 'network-error';
  return refusalOutcomes.find((slug) => isProblem(error, slug)) ?? 'unexpected';
}

/** Ends the submit span with the last response's status (none on a network error) and the outcome. */
export function endRaiseSubmit(submit: Span, error: unknown) {
  // 201 is the raise endpoint's only success status (openapi.json), and `customFetch` returns the body only.
  if (error === null || error === undefined) submit.setAttribute('http.response.status_code', 201);
  else if (error instanceof ApiError)
    submit.setAttribute('http.response.status_code', error.status);
  submit.setAttribute('dispute.raise.outcome', outcomeOf(error));
  submit.end();
}
