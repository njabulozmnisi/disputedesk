import type { QueryClient } from '@tanstack/react-query';
import { act, screen, waitFor } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import {
  getApiV1Transactions,
  getGetApiV1BffSessionQueryKey,
  getGetApiV1DisputesDisputeIdQueryKey,
  getGetApiV1DisputesInfiniteQueryKey,
  getGetApiV1TransactionsInfiniteQueryKey,
} from '@/api/generated/disputedesk';
import { ApiError, type Problem } from '@/api/mutator';
import { server } from '@/test/server';
import { created, openReviewScreen } from '@/test/raise';
import {
  disputeGuid,
  failWith,
  cachedCustomerData,
  customerQueries,
  holdSessionEndedRoute,
  listenToTabs,
  makeDispute,
  makeRow,
  noContent,
  problemResponse,
  renderApp,
  serveAntiforgeryToken,
  serveDispute,
  serveDisputes,
  serveTransactions,
  signInAs,
} from '@/test/support';

import type { AppRouter } from './router';
import { clearCustomerData, endSession } from './sessionEnd';

const sessionKey = getGetApiV1BffSessionQueryKey();

/** Any request outside the screens that answers 401, as the server does once the session has ended. */
async function aRequestAnswers401(queryClient: QueryClient) {
  failWith('/api/v1/transactions', 'unauthorized', 401);
  await act(() =>
    queryClient
      .query({ queryKey: ['probe'], queryFn: () => getApiV1Transactions() })
      .catch(() => undefined),
  );
}

/** Lets a late response arrive and its handlers run. */
async function letRequestsFinish() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });
}

function holdResponse() {
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => {
    release = resolve;
  });
  return {
    held,
    release: () => {
      release();
    },
  };
}

/** A router whose navigation completes only when `open` is called: the window in which a late response can arrive. */
function holdNavigation(router: AppRouter) {
  const gate = holdResponse();
  return {
    router: {
      state: router.state,
      navigate: (async (...args: Parameters<AppRouter['navigate']>) => {
        await gate.held;
        return router.navigate(...args);
      }) as AppRouter['navigate'],
    },
    open: gate.release,
  };
}

/** Puts the session and the row back, so that only a draft could still change what screen 4a shows, and opens 4a. */
async function reopenRaiseScreen(
  queryClient: QueryClient,
  router: ReturnType<typeof renderApp>['router'],
) {
  queryClient.setQueryData(sessionKey, { state: 'linked' });
  queryClient.setQueryData(getGetApiV1TransactionsInfiniteQueryKey(), {
    pages: [{ items: [makeRow()], nextCursor: null }],
    pageParams: [undefined],
  });
  await act(() =>
    router.navigate({
      to: '/transactions/$transactionId/dispute',
      params: { transactionId: 'TXN-1' },
    }),
  );
  await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' });
}

let tabs: ReturnType<typeof listenToTabs>;
beforeEach(() => {
  signInAs('linked');
  serveAntiforgeryToken();
  serveDisputes();
  serveTransactions();
  tabs = listenToTabs();
});
afterEach(() => {
  tabs.close();
});

// Criterion 1: one clean-up clears all customer data on every change to anonymous.
describe('a 401 while signed in', () => {
  it('removes every query but the session, clears the mutation cache, sets the session to anonymous and posts expired', async () => {
    serveTransactions([makeRow()]);
    const { queryClient, router } = renderApp({ path: '/transactions' });
    await screen.findAllByText('ZXP*GLOBALSVCS 0800');
    queryClient.setQueryData(getGetApiV1DisputesDisputeIdQueryKey(disputeGuid), makeDispute());
    queryClient.setQueryData(getGetApiV1DisputesInfiniteQueryKey(), { pages: [], pageParams: [] });
    queryClient
      .getMutationCache()
      .build(queryClient, { mutationFn: () => Promise.resolve('withdraw') });
    expect(queryClient.getMutationCache().getAll()).toHaveLength(1);
    await tabs.settle();

    await aRequestAnswers401(queryClient);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
    expect(cachedCustomerData(queryClient)).toEqual([]);
    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'anonymous' });
    expect(queryClient.getMutationCache().getAll()).toHaveLength(0);
    await tabs.flush();
    expect(tabs.messages).toEqual(['expired']);
  });

  it('resets the raise draft: coming back to the review screen finds no draft', async () => {
    const { queryClient, router } = await openReviewScreen();

    await aRequestAnswers401(queryClient);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
    // Put the row and the session back, so that only the draft could still send the person to the review screen.
    queryClient.setQueryData(sessionKey, { state: 'linked' });
    queryClient.setQueryData(getGetApiV1TransactionsInfiniteQueryKey(), {
      pages: [{ items: [makeRow()], nextCursor: null }],
      pageParams: [undefined],
    });
    await act(() =>
      router.navigate({
        to: '/transactions/$transactionId/dispute/review',
        params: { transactionId: 'TXN-1' },
      }),
    );

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 1, name: 'Review your dispute' }),
    ).not.toBeInTheDocument();
  });

  it('shows no customer data from memory when the person goes Back from the session-ended screen', async () => {
    // Not the start page's own sample merchant.
    serveTransactions([makeRow({ description: 'PRIVATE MERCHANT 4321' })]);
    const { queryClient, router } = renderApp({ path: '/transactions' });
    await screen.findAllByText('PRIVATE MERCHANT 4321');
    await aRequestAnswers401(queryClient);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });

    act(() => {
      router.history.back();
    });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/');
    });
    expect(screen.queryByText('PRIVATE MERCHANT 4321')).not.toBeInTheDocument();
    expect(queryClient.getQueryData(getGetApiV1TransactionsInfiniteQueryKey())).toBeUndefined();
  });

  it('writes nothing when the withdraw in flight then succeeds', async () => {
    serveDispute();
    const { user, queryClient, router } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: makeDispute().transaction.description });
    const response = holdResponse();
    server.use(
      http.post(`*/api/v1/disputes/${disputeGuid}/withdraw`, async () => {
        await response.held;
        return HttpResponse.json(makeDispute({ customerStatus: 'withdrawn' }));
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Withdraw dispute' }));
    await user.click(await screen.findByRole('button', { name: 'Withdraw dispute' }));
    await aRequestAnswers401(queryClient);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });

    response.release();
    await letRequestsFinish();

    expect(cachedCustomerData(queryClient)).toEqual([]);
    expect(router.state.location.pathname).toBe('/signed-out');
  });

  it('writes nothing and navigates nowhere when the raise in flight then succeeds', async () => {
    const { user, queryClient, router } = await openReviewScreen();
    const response = holdResponse();
    server.use(
      http.post('*/api/v1/disputes', async () => {
        await response.held;
        return created();
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Submit dispute' }));
    await aRequestAnswers401(queryClient);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });

    response.release();
    await letRequestsFinish();

    expect(cachedCustomerData(queryClient)).toEqual([]);
    expect(router.state.location.pathname).toBe('/signed-out');
    expect(screen.queryByText('Dispute submitted')).not.toBeInTheDocument();
    await reopenRaiseScreen(queryClient, router);
  });

  it('writes nothing and keeps the form clean when the raise in flight then answers 400', async () => {
    const { user, queryClient, router } = await openReviewScreen();
    const response = holdResponse();
    server.use(
      http.post('*/api/v1/disputes', async () => {
        await response.held;
        return problemResponse('validation', 400, 'trace-1', { errors: { reason: ['required'] } });
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Submit dispute' }));
    await aRequestAnswers401(queryClient);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });

    response.release();
    await letRequestsFinish();

    expect(router.state.location.pathname).toBe('/signed-out');
    await reopenRaiseScreen(queryClient, router);
    expect(screen.queryByRole('radio', { checked: true })).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('leaves the same state when the clean-up runs twice', async () => {
    const { queryClient } = renderApp({ path: '/transactions' });
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    queryClient.setQueryData(['customer data'], 'transactions');

    clearCustomerData(queryClient);
    const once = cachedCustomerData(queryClient);
    clearCustomerData(queryClient);

    expect(cachedCustomerData(queryClient)).toEqual(once);
    expect(once).toEqual([]);
    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'anonymous' });
  });
});

// Criterion 1: nothing comes back into memory while the session-ended screen is being shown.
describe('a response that arrives before the session-ended screen is shown', () => {
  it('brings no query data back', async () => {
    serveTransactions([makeRow()]);
    const { queryClient, router } = renderApp({ path: '/transactions' });
    await screen.findAllByText('ZXP*GLOBALSVCS 0800');
    const response = holdResponse();
    server.use(
      http.get('*/api/v1/disputes/late', async () => {
        await response.held;
        return HttpResponse.json({ secret: 'customer data' });
      }),
    );
    const late = queryClient
      .query({ queryKey: ['late'], queryFn: () => fetch('/api/v1/disputes/late') })
      .catch(() => undefined);
    const navigation = holdNavigation(router);

    const ended = endSession(queryClient, navigation.router);
    response.release();
    await late;
    await letRequestsFinish();

    expect(queryClient.getQueryData(['late'])).toBeUndefined();
    navigation.open();
    await ended;
    expect(cachedCustomerData(queryClient)).toEqual([]);
  });

  it('leaves the raise in flight no way to move the person or fill the draft', async () => {
    const { user, queryClient, router } = await openReviewScreen();
    const response = holdResponse();
    server.use(
      http.post('*/api/v1/disputes', async () => {
        await response.held;
        return created();
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Submit dispute' }));
    const navigation = holdNavigation(router);

    const ended = endSession(queryClient, navigation.router);
    response.release();
    await letRequestsFinish();
    navigation.open();
    await ended;
    await letRequestsFinish();

    expect(router.state.location.pathname).toBe('/signed-out');
    expect(cachedCustomerData(queryClient)).toEqual([]);
    expect(queryClient.getMutationCache().getAll()).toHaveLength(0);
    expect(screen.queryByText('Dispute submitted')).not.toBeInTheDocument();
  });
});

// Criterion 1: a session request answered anonymous ends the session as a 401 does.
describe('a session request answered anonymous while signed in', () => {
  it('runs the clean-up and posts expired once', async () => {
    serveTransactions([makeRow()]);
    const { queryClient, router } = renderApp({ path: '/transactions' });
    await screen.findAllByText('ZXP*GLOBALSVCS 0800');
    queryClient.setQueryData(getGetApiV1DisputesDisputeIdQueryKey(disputeGuid), makeDispute());
    queryClient
      .getMutationCache()
      .build(queryClient, { mutationFn: () => Promise.resolve('withdraw') });
    await tabs.settle();
    signInAs('anonymous');

    await act(() => queryClient.refetchQueries({ queryKey: sessionKey }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
    expect(cachedCustomerData(queryClient)).toEqual([]);
    expect(queryClient.getMutationCache().getAll()).toHaveLength(0);
    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'anonymous' });
    await tabs.flush();
    expect(tabs.messages).toEqual(['expired']);
  });
});

// Criterion 1: a response that arrives after a 401 restores nothing.
describe('a response that arrives after a 401', () => {
  async function fillDetailsAndContinue(user: UserEvent) {
    await user.click(await screen.findByLabelText('ID number'));
    await user.paste('8001015009087');
    await user.click(screen.getByLabelText('Account number'));
    await user.paste('1234567890');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
  }

  /** Starts a request that is held, ends the session with a 401, then lets the held answer arrive. */
  async function answerArrivesAfterA401(
    held: { method: 'get' | 'post'; path: string; answer: () => Response },
    startRequest: (app: ReturnType<typeof renderApp>) => Promise<void>,
  ) {
    const response = holdResponse();
    server.use(
      http[held.method](held.path, async () => {
        await response.held;
        return held.answer();
      }),
    );
    signInAs('unlinked');
    const app = renderApp({ path: '/link' });
    await screen.findByRole('heading', { level: 1, name: 'Link your login' });
    await startRequest(app);
    await aRequestAnswers401(app.queryClient);
    await waitFor(() => {
      expect(app.router.state.location.pathname).toBe('/signed-out');
    });
    serveTransactions(); // the 401 was for the probe only: a screen the late answer opens must not meet it again

    response.release();
    await letRequestsFinish();

    expect(app.queryClient.getQueryData(sessionKey)).toEqual({ state: 'anonymous' });
    expect(app.router.state.location.pathname).toBe('/signed-out');
  }

  it('does not link the login when the verify in flight then succeeds', async () => {
    await answerArrivesAfterA401(
      { method: 'post', path: '*/api/v1/linking/verify', answer: noContent },
      async ({ user, router }) => {
        await act(() => router.navigate({ to: '/link/otp' }));
        await user.click(await screen.findByLabelText('6-digit one-time code'));
        await user.paste('481516');
        await user.click(screen.getByRole('button', { name: 'Verify' }));
      },
    );
    expect(screen.queryByText('Your login is linked.')).not.toBeInTheDocument();
  });

  it('does not go to the code screen when the start in flight then succeeds', async () => {
    await answerArrivesAfterA401(
      {
        method: 'post',
        path: '*/api/v1/linking/start',
        answer: () => new HttpResponse(null, { status: 202 }),
      },
      async ({ user }) => {
        await fillDetailsAndContinue(user);
      },
    );
  });

  it('does not link the login when the start in flight then answers already-linked', async () => {
    await answerArrivesAfterA401(
      {
        method: 'post',
        path: '*/api/v1/linking/start',
        answer: () => problemResponse('already-linked', 409),
      },
      async ({ user }) => {
        await fillDetailsAndContinue(user);
      },
    );
  });

  it('does not make the session unlinked when a request in flight then answers 403 linking-required', async () => {
    await answerArrivesAfterA401(
      {
        method: 'get',
        path: '*/api/v1/disputes/late',
        answer: () => problemResponse('linking-required', 403),
      },
      ({ queryClient }) => {
        void queryClient
          .getMutationCache()
          .build(queryClient, {
            mutationFn: async () => {
              const reply = await fetch('/api/v1/disputes/late');
              throw new ApiError(reply.status, (await reply.json()) as Problem);
            },
          })
          .execute(undefined)
          .catch(() => undefined);
        return Promise.resolve();
      },
    );
  });
});

// Criterion 1: customer data is gone from the moment the session starts ending, not when the screen has changed.
describe('a session that has started ending', () => {
  it('holds no customer query data and no mutation, and starts no request, while the screen is still changing', async () => {
    serveTransactions([makeRow()]);
    const { queryClient, router } = renderApp({ path: '/transactions' });
    await screen.findAllByText('ZXP*GLOBALSVCS 0800');
    queryClient
      .getMutationCache()
      .build(queryClient, { mutationFn: () => Promise.resolve('withdraw') });
    const requests: string[] = [];
    server.events.on('request:start', ({ request }) => {
      requests.push(new URL(request.url).pathname);
    });
    const navigation = holdNavigation(router);

    const ended = endSession(queryClient, navigation.router);
    await letRequestsFinish();

    expect(cachedCustomerData(queryClient)).toEqual([]);
    expect(queryClient.getMutationCache().getAll()).toHaveLength(0);
    expect(requests).toEqual([]);
    // Only the session query still says signed in, so that SessionGate does not race the session-ended screen.
    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'linked' });
    navigation.open();
    await ended;
    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'anonymous' });
  });
});

// Criterion 1 and 4: a session answer that lands after the clean-up restores nothing.
describe('a session answer that arrives after the session ended', () => {
  it('is not written back as signed in', async () => {
    serveTransactions([makeRow()]);
    const { queryClient, router } = renderApp({ path: '/transactions' });
    await screen.findAllByText('ZXP*GLOBALSVCS 0800');
    const response = holdResponse();
    server.use(
      http.get('*/api/v1/bff/session', async () => {
        await response.held;
        return HttpResponse.json({ state: 'linked' });
      }),
    );
    const refetch = queryClient.refetchQueries({ queryKey: sessionKey }).catch(() => undefined);

    await act(() => endSession(queryClient, router));
    response.release();
    await refetch;
    await letRequestsFinish();

    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'anonymous' });
    expect(router.state.location.pathname).toBe('/signed-out');
  });
});

// Criteria 1 and 4: no customer query is fetched or left behind once the session ends (ASVS 5.0.0 V14.3.1).
describe('a screen still mounted while the session-ended navigation is pending', () => {
  it('starts no customer request and leaves no customer query: transactions', async () => {
    serveTransactions([makeRow()]);
    const { queryClient, router } = renderApp({ path: '/transactions' });
    await screen.findAllByText('ZXP*GLOBALSVCS 0800');
    const requests: string[] = [];
    server.events.on('request:start', ({ request }) => {
      requests.push(new URL(request.url).pathname);
    });
    const open = holdSessionEndedRoute(router);

    const ended = endSession(queryClient, router);
    await letRequestsFinish();
    expect(requests).toEqual([]);

    open();
    await act(() => ended);
    await letRequestsFinish();
    expect(router.state.location.pathname).toBe('/signed-out');
    expect(requests).toEqual([]);
    expect(customerQueries(queryClient)).toEqual([]);
  });

  it('starts no customer request and leaves no customer query: dispute detail just submitted', async () => {
    serveDispute();
    const { queryClient, router } = renderApp({ path: '/disputes' });
    await act(() =>
      router.navigate({
        to: '/disputes/$disputeId',
        params: { disputeId: disputeGuid },
        state: { justSubmitted: true },
      }),
    );
    await screen.findByText('Dispute submitted');
    const requests: string[] = [];
    server.events.on('request:start', ({ request }) => {
      requests.push(new URL(request.url).pathname);
    });
    const open = holdSessionEndedRoute(router);

    const ended = endSession(queryClient, router);
    await letRequestsFinish();
    expect(requests).toEqual([]);

    open();
    await act(() => ended);
    await letRequestsFinish();
    expect(requests).toEqual([]);
    expect(customerQueries(queryClient)).toEqual([]);
  });
});

// Criterion 1: one end per window.
describe('a second anonymous session answer while the session is ending', () => {
  it('posts expired once', async () => {
    serveTransactions([makeRow()]);
    const { queryClient, router } = renderApp({ path: '/transactions' });
    await screen.findAllByText('ZXP*GLOBALSVCS 0800');
    await tabs.settle();
    const gate = holdResponse();
    const navigate = router.navigate.bind(router);
    vi.spyOn(router, 'navigate').mockImplementation((async (
      ...args: Parameters<AppRouter['navigate']>
    ) => {
      await gate.held;
      return navigate(...args);
    }) as AppRouter['navigate']);
    signInAs('anonymous');

    await act(() => queryClient.refetchQueries({ queryKey: sessionKey }));
    await act(() => queryClient.refetchQueries({ queryKey: sessionKey }));
    gate.release();
    await letRequestsFinish();

    await tabs.flush();
    // The screen the end leaves may still post activity for its own responses; only expired is under test.
    expect(tabs.messages.filter((message) => message === 'expired')).toEqual(['expired']);
  });
});
