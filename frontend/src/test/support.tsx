import { QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router';
import { http, HttpResponse } from 'msw';
import { StrictMode } from 'react';

import type {
  DisputeResponse,
  DisputeSummaryResponse,
  SessionState,
  TransactionResponse,
} from '@/api/generated/model';
import { problemType, type ProblemSlug } from '@/api/problems';
import { createQueryClient } from '@/api/queryClient';
import { createAppRouter } from '@/app/router';

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
  render(strictMode ? <StrictMode>{app}</StrictMode> : app);
  return { router, queryClient, user };
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

/** The 201 body of a raised dispute, for the row's transaction. */
export function makeDispute(overrides: Partial<DisputeResponse> = {}): DisputeResponse {
  const row = makeRow();
  return {
    disputeId: 'D-1',
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
    timeline: [],
    ...overrides,
  };
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
