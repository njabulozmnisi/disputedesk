import { ApiError } from './mutator';

// The problem `type` is a URI that identifies the kind of problem (RFC 9457 section 3.1.1), so the SPA compares the
// whole string. It builds the URI from this one base and a slug (AD-3), and never parses, links or fetches it.
const problemTypeBase = 'https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems';

/** The slugs the SPA acts on. Add one when a screen needs to tell that problem apart. */
export type ProblemSlug =
  | 'linking-required'
  | 'already-linked'
  | 'otp-incorrect'
  | 'linking-locked'
  | 'rate-limited'
  | 'core-banking-unavailable'
  | 'validation'
  | 'not-found'
  | 'already-disputed'
  | 'not-disputable'
  | 'idempotency-in-progress'
  | 'idempotency-mismatch'
  | 'action-not-allowed';

export function problemType(slug: ProblemSlug): string {
  return `${problemTypeBase}/${slug}.md`;
}

export function isProblem(error: unknown, slug: ProblemSlug): boolean {
  return error instanceof ApiError && error.problem?.type === problemType(slug);
}

/** The reference to quote to the bank, when the error is a response whose problem body carried one. */
export function traceIdOf(error: ApiError | TypeError): string | undefined {
  return error instanceof ApiError ? error.problem?.traceId : undefined;
}

/** A string extension member of the problem body (RFC 9457 section 3.2), such as `disputeId` or `reason`. */
export function problemExtension(
  error: ApiError,
  name: 'disputeId' | 'reason' | 'customerStatus',
): string | undefined {
  const value = (error.problem as Record<string, unknown> | undefined)?.[name];
  return typeof value === 'string' ? value : undefined;
}

/** An integer extension member of the problem body (RFC 9457 section 3.2), such as `attemptsRemaining`. */
export function problemInteger(error: ApiError, name: 'attemptsRemaining'): number | undefined {
  const value = (error.problem as Record<string, unknown> | undefined)?.[name];
  return Number.isInteger(value) ? (value as number) : undefined;
}

/** The names of the fields a 400 `validation` problem lists in its `errors` member (RFC 9457 section 3.2). */
export function validationFields(error: ApiError): string[] {
  const named = (error.problem as { errors?: Record<string, unknown> } | undefined)?.errors;
  return named ? Object.keys(named) : [];
}
