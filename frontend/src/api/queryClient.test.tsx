import { focusManager, onlineManager, QueryClientProvider } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { ReactNode } from 'react';

import { createAppRouter } from '@/app/router';
import { server } from '@/test/server';
import { failWith, problemResponse, serveAntiforgeryToken, signInAs } from '@/test/support';

import {
  getGetSessionQueryKey,
  getGetSessionQueryOptions,
  useLogout,
  useListTransactions,
} from './generated/disputedesk';
import { createQueryClient } from './queryClient';

function setUp(sessionState?: 'anonymous' | 'unlinked' | 'linked', path = '/transactions') {
  const queryClient = createQueryClient(() => router);
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  queryClient.setDefaultOptions({
    ...queryClient.getDefaultOptions(),
    queries: { ...queryClient.getDefaultOptions().queries, retryDelay: 0 },
  });
  if (sessionState) {
    queryClient.setQueryData(getGetSessionQueryKey(), { state: sessionState });
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

const sessionKey = getGetSessionQueryKey();

describe('retries (criterion 2)', () => {
  it('retries no 4xx', async () => {
    const requests = countRequests('/api/v1/transactions', () =>
      problemResponse('unauthorized', 400),
    );
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => useListTransactions(), { wrapper });

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

    const { result } = renderHook(() => useListTransactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(requests.calls).toBe(2);
  });

  it('retries a read once on a network error', async () => {
    const requests = countRequests('/api/v1/transactions', () => HttpResponse.error());
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => useListTransactions(), { wrapper });

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

    const { result } = renderHook(() => useListTransactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });
    expect(calls).toBe(2);
  });

  it('never retries a write', async () => {
    serveAntiforgeryToken();
    const requests = countRequests('/api/v1/authentication/logout', () =>
      problemResponse('core-banking-unavailable', 503),
    );
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => useLogout(), { wrapper });
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
    const { result } = renderHook(() => useListTransactions(), { wrapper });
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
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends a 403 linking-required to /link', async () => {
    failWith('/api/v1/transactions', 'linking-required', 403);
    const { router, wrapper } = setUp('unlinked');

    renderHook(() => useListTransactions(), { wrapper });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/link');
    });
  });

  it('ends a linked session in the cache on a 403 linking-required, before it goes to /link', async () => {
    failWith('/api/v1/transactions', 'linking-required', 403);
    const { router, queryClient, wrapper } = setUp('linked');

    renderHook(() => useListTransactions(), { wrapper });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/link');
    });
    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'unlinked' });
  });

  it('does not treat a 403 of another kind as linking-required', async () => {
    failWith('/api/v1/transactions', 'unauthorized', 403);
    const { router, wrapper } = setUp('linked');

    const { result } = renderHook(() => useListTransactions(), { wrapper });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(router.state.location.pathname).toBe('/transactions');
  });

  it('ends the session on a 401 from a mutation: the browser leaves for the session-ended screen', async () => {
    const replace = vi.fn();
    vi.stubGlobal('location', { pathname: '/transactions', search: '', replace });
    serveAntiforgeryToken();
    failWith('/api/v1/authentication/logout', 'unauthorized', 401);
    const { wrapper } = setUp('linked');

    const { result } = renderHook(() => useLogout(), { wrapper });
    result.current.mutate();

    await waitFor(() => {
      expect(replace).toHaveBeenCalledTimes(1);
    });
  });
});

// A session answer fetched from the server is the only thing that can show the session ended elsewhere (QueryCache
// onSuccess runs for fetched data, never for setQueryData).
describe('a session answer in the QueryCache', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function fetchSession(queryClient: ReturnType<typeof setUp>['queryClient']) {
    return queryClient.query({ ...getGetSessionQueryOptions(), staleTime: 0 });
  }

  it('leaves the document when a signed-in session is later answered anonymous', async () => {
    const replace = vi.fn();
    vi.stubGlobal('location', { pathname: '/transactions', search: '', replace });
    const { queryClient } = setUp();
    signInAs('linked');
    await fetchSession(queryClient);
    expect(replace).not.toHaveBeenCalled();

    signInAs('anonymous');
    await fetchSession(queryClient);

    expect(replace).toHaveBeenCalledTimes(1);
  });

  it('does not leave when the first answer is anonymous', async () => {
    const replace = vi.fn();
    vi.stubGlobal('location', { pathname: '/', search: '', replace });
    const { queryClient } = setUp();
    signInAs('anonymous');

    await fetchSession(queryClient);
    await fetchSession(queryClient);

    expect(replace).not.toHaveBeenCalled();
  });

  it('does not leave when the app itself sets the session to anonymous', async () => {
    const replace = vi.fn();
    vi.stubGlobal('location', { pathname: '/', search: '', replace });
    const { queryClient } = setUp();
    signInAs('linked');
    await fetchSession(queryClient);

    queryClient.setQueryData(sessionKey, { state: 'anonymous' });

    expect(replace).not.toHaveBeenCalled();
  });
});
