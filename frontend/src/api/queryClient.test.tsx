import {
  focusManager,
  onlineManager,
  QueryClientProvider,
  type QueryClient,
} from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { ReactNode } from 'react';

import { createAppRouter } from '@/app/router';
import { server } from '@/test/server';
import { failWith, problemResponse, serveAntiforgeryToken } from '@/test/support';

import {
  getGetApiV1BffSessionQueryKey,
  usePostApiV1BffLogout,
  useGetApiV1Transactions,
} from './generated/disputedesk';
import { createQueryClient } from './queryClient';

function setUp(sessionState?: 'anonymous' | 'unlinked' | 'linked', path = '/transactions') {
  const router = createAppRouter(createMemoryHistory({ initialEntries: [path] }));
  const queryClient = createQueryClient(router);
  queryClient.setDefaultOptions({
    ...queryClient.getDefaultOptions(),
    queries: { ...queryClient.getDefaultOptions().queries, retryDelay: 0 },
  });
  if (sessionState) {
    queryClient.setQueryData(getGetApiV1BffSessionQueryKey(), { state: sessionState });
  }
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return { router, queryClient, wrapper };
}

function countRequests(path: string, respond: () => Response) {
  const counter = { calls: 0 };
  server.use(
    http.all(`*${path}`, () => {
      counter.calls += 1;
      return respond();
    }),
  );
  return counter;
}

const sessionKey = getGetApiV1BffSessionQueryKey();
const hasSession = (queryClient: QueryClient) => queryClient.getQueryData(sessionKey);

describe('retries (criterion 2)', () => {
  it('retries no 4xx', async () => {
    const requests = countRequests('/api/v1/transactions', () =>
      problemResponse('unauthorized', 400),
    );
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => useGetApiV1Transactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(requests.calls).toBe(1);
  });

  it('retries a read once on a 503', async () => {
    const requests = countRequests('/api/v1/transactions', () =>
      problemResponse('core-banking-unavailable', 503),
    );
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => useGetApiV1Transactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(requests.calls).toBe(2);
  });

  it('retries a read once on a network error', async () => {
    const requests = countRequests('/api/v1/transactions', () => HttpResponse.error());
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => useGetApiV1Transactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(requests.calls).toBe(2);
  });

  it('retries a read on a 503 and shows the data when the retry succeeds', async () => {
    let calls = 0;
    server.use(
      http.get('*/api/v1/transactions', () => {
        calls += 1;
        return calls === 1
          ? problemResponse('core-banking-unavailable', 503)
          : HttpResponse.json({ items: [], nextCursor: null });
      }),
    );
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => useGetApiV1Transactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });
    expect(calls).toBe(2);
  });

  it('never retries a write', async () => {
    serveAntiforgeryToken();
    const requests = countRequests('/api/v1/bff/logout', () =>
      problemResponse('core-banking-unavailable', 503),
    );
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => usePostApiV1BffLogout(), { wrapper });
    result.current.mutate();

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(requests.calls).toBe(1);
  });
});

// An automatic refetch would renew the server's idle limit without the person acting (AD-7, OWASP idle timeout).
describe('no refetch the person did not ask for', () => {
  afterEach(() => {
    focusManager.setFocused(undefined);
    onlineManager.setOnline(true);
  });

  async function mountedTransactions() {
    const requests = countRequests('/api/v1/transactions', () =>
      HttpResponse.json({ items: [], nextCursor: null }),
    );
    const { wrapper } = setUp('linked');
    const { result } = renderHook(() => useGetApiV1Transactions(), { wrapper });
    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });
    return requests;
  }

  it('does not refetch when the window regains focus', async () => {
    const requests = await mountedTransactions();

    focusManager.setFocused(false);
    focusManager.setFocused(true);
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(requests.calls).toBe(1);
  });

  it('does not refetch when the network comes back', async () => {
    const requests = await mountedTransactions();

    onlineManager.setOnline(false);
    onlineManager.setOnline(true);
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(requests.calls).toBe(1);
  });
});

describe('errors handled once in the QueryCache and MutationCache (criterion 2)', () => {
  it('sends a 403 linking-required to /link', async () => {
    failWith('/api/v1/transactions', 'linking-required', 403);
    const { router, wrapper } = setUp('unlinked');

    renderHook(() => useGetApiV1Transactions(), { wrapper });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/link');
    });
  });

  it('does not treat a 403 of another kind as linking-required', async () => {
    failWith('/api/v1/transactions', 'unauthorized', 403);
    const { router, wrapper } = setUp('linked');

    const { result } = renderHook(() => useGetApiV1Transactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(router.state.location.pathname).toBe('/transactions');
  });

  it('sends a 401 while signed in to /signed-out?reason=expired and ends the session in the cache', async () => {
    failWith('/api/v1/transactions', 'unauthorized', 401);
    const { router, queryClient, wrapper } = setUp('linked');

    renderHook(() => useGetApiV1Transactions(), { wrapper });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
    expect(router.state.location.search).toEqual({
      reason: 'expired',
      returnUrl: '/transactions',
    });
    expect(hasSession(queryClient)).toEqual({ state: 'anonymous' });
  });

  // Story 3.6 criterion 8 (AD-31): no identifier from a raise route reaches the BFF login URL.
  it.each(['/transactions/TXN-123/dispute', '/transactions/TXN-123/dispute/review'])(
    'returns to /transactions after a 401 on %s, so the transaction ID is not in returnUrl',
    async (path) => {
      failWith('/api/v1/transactions', 'unauthorized', 401);
      const { router, wrapper } = setUp('linked', path);

      renderHook(() => useGetApiV1Transactions(), { wrapper });

      await waitFor(() => {
        expect(router.state.location.pathname).toBe('/signed-out');
      });
      expect(router.state.location.search).toEqual({
        reason: 'expired',
        returnUrl: '/transactions',
      });
    },
  );

  it('sends a 401 from a mutation to the session-ended screen too', async () => {
    serveAntiforgeryToken();
    failWith('/api/v1/bff/logout', 'unauthorized', 401);
    const { router, wrapper } = setUp('linked');

    const { result } = renderHook(() => usePostApiV1BffLogout(), { wrapper });
    result.current.mutate();

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
  });

  it('leaves a 401 alone when nobody is signed in', async () => {
    failWith('/api/v1/transactions', 'unauthorized', 401);
    const { router, wrapper } = setUp('anonymous');

    const { result } = renderHook(() => useGetApiV1Transactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(router.state.location.pathname).toBe('/transactions');
  });
});
