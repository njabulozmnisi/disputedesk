import { ApiError } from './mutator';

// The problem `type` is a URI that identifies the kind of problem (RFC 9457 section 3.1.1), so the SPA compares the
// whole string. It builds the URI from this one base and a slug (AD-3), and never parses, links or fetches it.
const problemTypeBase = 'https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems';

/** The slugs the SPA acts on. Add one when a screen needs to tell that problem apart. */
export type ProblemSlug = 'linking-required' | 'core-banking-unavailable';

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
