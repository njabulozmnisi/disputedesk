import { hashKey, QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router';
import { http, HttpResponse } from 'msw';
import { StrictMode } from 'react';
import { afterEach } from 'vitest';

import type {
  DisputeResponse,
  DisputeSummaryResponse,
  SessionState,
  TransactionResponse,
} from '@/api/generated/model';
import { getGetApiV1BffSessionQueryKey } from '@/api/generated/disputedesk';
import { problemType, type ProblemSlug } from '@/api/problems';
import { createQueryClient } from '@/api/queryClient';
import { createAppRouter, type AppRouter } from '@/app/router';

import { server } from './server';

type RenderOptions = {
  path?: string;
  /** Use fake timers: the user-event clock then follows them. */
  fakeTimers?: boolean;
  /** Render inside `<StrictMode>`, as `main.tsx` does. */
  strictMode?: boolean;
};

/**
 * Renders the whole app at `path` with a memory history and a real QueryClient. The API is whatever handlers the test
 * has added to the MSW server. Reads retry with no delay so a 503 test does not wait a second.
 */
export function renderApp({
  path = '/',
  fakeTimers = false,
  strictMode = false,
}: RenderOptions = {}) {
  const router = createAppRouter(createMemoryHistory({ initialEntries: [path] }));
  const queryClient = createQueryClient(router);
  queryClient.setDefaultOptions({
    ...queryClient.getDefaultOptions(),
    queries: { ...queryClient.getDefaultOptions().queries, retryDelay: 0 },
  });
  const user = userEvent.setup(fakeTimers ? { advanceTimers: vi.advanceTimersByTime } : undefined);
  const app = (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
  const { unmount } = render(strictMode ? <StrictMode>{app}</StrictMode> : app);
  return { router, queryClient, user, unmount };
}

/** Every cached query but the session's, with or without data. */
export function customerQueries(queryClient: QueryClient) {
  return queryClient
    .getQueryCache()
    .getAll()
    .filter((query) => query.queryHash !== hashKey(getGetApiV1BffSessionQueryKey()));
}

/**
 * The hash of every cached query but the session's that holds data. A screen still mounted for a moment after a
 * clean-up may leave an empty query behind; it holds no customer data and is collected once nothing observes it.
 */
export function cachedCustomerData(queryClient: QueryClient): string[] {
  return queryClient
    .getQueryCache()
    .getAll()
    .filter((query) => query.queryHash !== hashKey(getGetApiV1BffSessionQueryKey()))
    .filter((query) => query.state.data !== undefined)
    .map((query) => query.queryHash);
}

/**
 * A tab of the test's own on the session channel. It records what the app instances post. `flush` posts a message
 * no instance knows and waits for it to come back: channel messages arrive in order, so everything posted before it
 * has arrived by then.
 */
export function listenToTabs() {
  const listener = new BroadcastChannel('disputedesk-session');
  const sender = new BroadcastChannel('disputedesk-session');
  const messages: unknown[] = [];
  let seenFlush: () => void = () => undefined;
  listener.onmessage = (event: MessageEvent) => {
    if (event.data === 'flush') seenFlush();
    else messages.push(event.data);
  };
  async function flush() {
    const arrived = new Promise<void>((resolve) => {
      seenFlush = resolve;
    });
    sender.postMessage('flush');
    await arrived;
  }
  return {
    messages,
    post: (message: unknown) => {
      sender.postMessage(message);
    },
    flush,
    /** Waits for what was posted so far, then forgets it: page loads post activity. */
    settle: async () => {
      await flush();
      messages.length = 0;
    },
    close: () => {
      listener.close();
      sender.close();
    },
  };
}

/** The session endpoint answers with `state`. */
export function signInAs(state: SessionState) {
  server.use(http.get('*/api/v1/bff/session', () => HttpResponse.json({ state })));
}

/** The antiforgery endpoint hands out `requestToken`. */
export function serveAntiforgeryToken(requestToken = 'request-token') {
  server.use(http.get('*/api/v1/bff/antiforgery', () => HttpResponse.json({ requestToken })));
}

/** The transactions endpoint answers with one page of `items` and no next page. */
export function serveTransactions(items: TransactionResponse[] = []) {
  server.use(
    http.get('*/api/v1/transactions', () => HttpResponse.json({ items, nextCursor: null })),
  );
}

/** The disputes endpoint answers with one page of `items` and no next page. */
export function serveDisputes(items: DisputeSummaryResponse[] = []) {
  server.use(http.get('*/api/v1/disputes', () => HttpResponse.json({ items, nextCursor: null })));
}

/** A problem response (RFC 9457) as the API writes it: the full `type` URI, a status and a `traceId`. */
export function problemResponse(
  slug: ProblemSlug | 'unauthorized' | 'internal',
  status: number,
  traceId = 'trace-1',
  extensions: Record<string, unknown> = {},
) {
  const type =
    slug === 'unauthorized' || slug === 'internal'
      ? `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/${slug}.md`
      : problemType(slug);
  return HttpResponse.json(
    { type, title: slug, status, traceId, ...extensions },
    { status, headers: { 'content-type': 'application/problem+json' } },
  );
}

/** Replies to every request for `path` (any method) with a problem. */
export function failWith(path: string, slug: ProblemSlug | 'unauthorized', status: number) {
  server.use(http.all(`*${path}`, () => problemResponse(slug, status)));
}

/** One transaction row with sensible defaults: a settled card debit that can be disputed. */
export function makeRow(overrides: Partial<TransactionResponse> = {}): TransactionResponse {
  return {
    transactionId: 'TXN-1',
    accountRef: 'ACC-1',
    accountLast4: '4821',
    transactionDate: '2026-10-26T09:14:00Z',
    description: 'ZXP*GLOBALSVCS 0800',
    amount: { amount: '849.00', currency: 'ZAR' },
    direction: 'debit',
    kind: 'card',
    state: 'settled',
    maskedCard: '492345******3456',
    disputable: true,
    notDisputableReason: null,
    disputeId: null,
    customerStatus: null,
    ...overrides,
  };
}

/** The ID of `makeDispute()`: a canonical GUID, as the detail route accepts only that. */
export const disputeGuid = '11111111-1111-4111-8111-111111111111';

/** The 201 body of a raised dispute, for the row's transaction. */
export function makeDispute(overrides: Partial<DisputeResponse> = {}): DisputeResponse {
  const row = makeRow();
  return {
    disputeId: disputeGuid,
    customerStatus: 'submitted',
    outcome: null,
    reason: 'unrecognised',
    note: null,
    transaction: {
      transactionId: row.transactionId,
      accountRef: row.accountRef,
      accountLast4: row.accountLast4,
      transactionDate: row.transactionDate,
      description: row.description,
      amount: row.amount,
      maskedCard: row.maskedCard,
    },
    expectedAmount: null,
    disputedAmount: row.amount,
    createdAt: '2026-10-30T08:00:00Z',
    timeline: [
      {
        action: 'raise',
        customerStatus: 'submitted',
        actor: 'you',
        occurredAt: '2026-10-30T08:00:00Z',
        note: null,
      },
    ],
    ...overrides,
  };
}

/** The detail endpoint answers with `dispute` for its ID. */
export function serveDispute(dispute: DisputeResponse = makeDispute()) {
  server.use(http.get(`*/api/v1/disputes/${dispute.disputeId}`, () => HttpResponse.json(dispute)));
}

export type WithdrawRequest = { url: string; headers: Headers; body: unknown };

/** Answers `POST .../withdraw` with `respond(attempt)` (1 for the first request) and records every request. */
export function recordWithdrawRequests(
  respond: (attempt: number) => Response | Promise<Response>,
  disputeId = disputeGuid,
) {
  const requests: WithdrawRequest[] = [];
  server.use(
    http.post(`*/api/v1/disputes/${disputeId}/withdraw`, async ({ request }) => {
      requests.push({ url: request.url, headers: request.headers, body: await request.json() });
      return respond(requests.length);
    }),
  );
  return requests;
}

/** One row of the disputes list: a submitted dispute with sensible defaults. */
export function makeSummary(
  overrides: Partial<DisputeSummaryResponse> = {},
): DisputeSummaryResponse {
  const dispute = makeDispute();
  return {
    disputeId: dispute.disputeId,
    customerStatus: 'submitted',
    outcome: null,
    reason: 'unrecognised',
    transaction: dispute.transaction,
    disputedAmount: dispute.disputedAmount,
    createdAt: dispute.createdAt,
    ...overrides,
  };
}

export type LinkingRequest = { headers: Headers; body: unknown };

/**
 * Answers `POST /api/v1/linking/start` or `.../verify` with `respond(attempt)` (1 for the first request) and records
 * every request.
 */
export function recordLinkingRequests(
  step: 'start' | 'verify',
  respond: (attempt: number) => Response | Promise<Response>,
) {
  const requests: LinkingRequest[] = [];
  server.use(
    http.post(`*/api/v1/linking/${step}`, async ({ request }) => {
      requests.push({ headers: request.headers, body: await request.json() });
      return respond(requests.length);
    }),
  );
  return requests;
}

/** The empty answers: 202 to a start, 204 to a verify. */
export const accepted = () => new HttpResponse(null, { status: 202 });
export const noContent = () => new HttpResponse(null, { status: 204 });

/** A 429 as the API or the gateway sends it: a `rate-limited` problem, with `Retry-After` when given. */
export function rateLimited(retryAfter?: string) {
  const response = problemResponse('rate-limited', 429);
  if (retryAfter !== undefined) response.headers.set('Retry-After', retryAfter);
  return response;
}

// Puts back every session-ended route held by a test, so one that fails before it lets the route finish cannot leave it
// held for the next test: the route tree is shared by every app instance in the run.
const restoreHeldRoutes: (() => void)[] = [];
afterEach(() => {
  restoreHeldRoutes.splice(0).forEach((restore) => {
    restore();
  });
});

/**
 * Holds the session-ended route's `beforeLoad`, so a navigation to it stays pending as a real one does while the
 * screens still mounted re-render. Returns the function that lets it finish; the route is restored then, or after the
 * test at the latest.
 */
export function holdSessionEndedRoute(router: AppRouter): () => void {
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => {
    release = resolve;
  });
  const options = router.routesByPath['/signed-out'].options;
  const beforeLoad = options.beforeLoad;
  // The route's own `beforeLoad` is typed to return nothing, but the router awaits whatever it returns.
  const holdingBeforeLoad = async (context: Parameters<NonNullable<typeof beforeLoad>>[0]) => {
    await held;
    beforeLoad?.(context);
  };
  Object.defineProperty(options, 'beforeLoad', { value: holdingBeforeLoad, configurable: true });
  const restore = () => {
    release();
    Object.defineProperty(options, 'beforeLoad', { value: beforeLoad, configurable: true });
  };
  restoreHeldRoutes.push(restore);
  return restore;
}
