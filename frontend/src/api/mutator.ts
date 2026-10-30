import { context, type Context } from '@opentelemetry/api';

import type { ProblemDetails } from './generated/model';

/** A problem body (RFC 9457). `traceId` is the extension member every DisputeDesk problem carries (AD-3). */
export type Problem = ProblemDetails & { traceId?: string };

/** Thrown for every non-2xx response. `problem` is undefined when the body is not a problem (a gateway page, say). */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: Problem | undefined;
  /** The raw `Retry-After` header (RFC 9110 section 10.2.3), or null when the response had none. */
  readonly retryAfter: string | null;

  constructor(status: number, problem: Problem | undefined, retryAfter: string | null = null) {
    super(`API request failed with status ${String(status)}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.retryAfter = retryAfter;
  }
}

/**
 * Orval reads this export to type the `error` of every generated hook: an error response is an `ApiError`, and a
 * network failure reaches the hook as the `TypeError` that `fetch` rejects with.
 */
export type ErrorType<_ErrorData> = ApiError | TypeError;

const antiforgeryUrl = '/api/v1/bff/antiforgery';

// Safe methods never change state, so they need no antiforgery token (RFC 9110 section 9.2.1).
const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

type ResponseListener = (status: number) => void;
const responseListeners = new Set<ResponseListener>();

/** Calls `listener` with the status of every API response that arrives. Returns the unsubscribe function. */
export function onApiResponse(listener: ResponseListener): () => void {
  responseListeners.add(listener);
  return () => responseListeners.delete(listener);
}

async function readProblem(response: Response): Promise<Problem | undefined> {
  if (!response.headers.get('content-type')?.includes('application/problem+json')) return undefined;
  return (await response.json()) as Problem;
}

// The tracing context manager follows synchronous code only, so a fetch after an `await` would lose the caller's span.
// `send` therefore runs each fetch inside the context that `customFetch` read before its first `await` (OpenTelemetry JS,
// Context: manual context propagation).
async function send(url: string, options: RequestInit, callerContext: Context): Promise<Response> {
  const response = await context.with(callerContext, () =>
    fetch(url, { ...options, credentials: 'same-origin' }),
  );
  responseListeners.forEach((listener) => {
    listener(response.status);
  });
  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(response.status, problem, response.headers.get('Retry-After'));
  }
  return response;
}

/** The one Orval mutator: same-origin credentials, and the antiforgery token on unsafe methods only. */
export const customFetch = async <T>(url: string, options: RequestInit): Promise<T> => {
  const callerContext = context.active();
  const headers = new Headers(options.headers);
  if (!safeMethods.has((options.method ?? 'GET').toUpperCase())) {
    const tokenResponse = await send(antiforgeryUrl, {}, callerContext);
    const { requestToken } = (await tokenResponse.json()) as { requestToken: string };
    headers.set('X-XSRF-TOKEN', requestToken);
  }

  const response = await send(url, { ...options, headers }, callerContext);
  const hasJsonBody = response.headers.get('content-type')?.includes('json') ?? false;
  return (hasJsonBody ? await response.json() : undefined) as T;
};
