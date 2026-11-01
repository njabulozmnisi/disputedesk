import type { Query } from '@tanstack/react-query';
import { act, cleanup, screen, waitFor } from '@testing-library/react';
import { setFlagsFromString } from 'node:v8';
import { runInNewContext } from 'node:vm';

import { getGetApiV1TransactionsInfiniteQueryKey } from '@/api/generated/disputedesk';
import {
  holdSessionEndedRoute,
  listenToTabs,
  makeRow,
  renderApp,
  serveAntiforgeryToken,
  serveTransactions,
  signInAs,
} from '@/test/support';

import { endSession } from './sessionEnd';

// Criterion 1 (ASVS 5.0.0 V14.3.1): once the session ends and the screen is gone, nothing keeps the customer's query
// reachable. A query removed from the cache while a screen still observes it is kept alive by that observer, so each
// way a session ends is checked with a WeakRef and a forced garbage collection (Node's `--expose_gc`).
setFlagsFromString('--expose_gc');
const collectGarbage = runInNewContext('gc') as () => void;

async function collect() {
  // Node's fetch keeps the AbortSignal of the last request it made, and that signal's listener keeps the query that made
  // it, until another request is made (observed with undici under Vitest); one more request lets the query go.
  await fetch('/api/v1/bff/antiforgery');
  for (let pass = 0; pass < 5; pass++) {
    collectGarbage();
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

async function letRequestsFinish() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });
}

/** Signs in on the transactions screen and returns a weak reference to its query, so the test itself keeps none. */
async function openTransactions() {
  signInAs('linked');
  serveAntiforgeryToken();
  serveTransactions([makeRow()]);
  const app = renderApp({ path: '/transactions' });
  await screen.findAllByText('ZXP*GLOBALSVCS 0800');
  const query = app.queryClient
    .getQueryCache()
    .find({ queryKey: getGetApiV1TransactionsInfiniteQueryKey() }) as Query;
  return { ...app, queryRef: new WeakRef(query) };
}

async function expectCollected(queryRef: WeakRef<Query>) {
  cleanup(); // unmounts every screen
  await collect();
  expect(queryRef.deref()).toBeUndefined();
}

describe('the customer query after the session ends and the screen is gone', () => {
  it('is collected after endSession, with the session-ended navigation pending', async () => {
    const { queryClient, router, queryRef } = await openTransactions();
    const open = holdSessionEndedRoute(router);

    const ended = endSession(queryClient, router);
    await letRequestsFinish();
    open();
    await act(() => ended);
    await letRequestsFinish();

    await expectCollected(queryRef);
  });

  it('is collected after a signed-out notice from another tab', async () => {
    const tabs = listenToTabs();
    const { router, queryRef } = await openTransactions();
    await tabs.settle();

    tabs.post('signed-out');
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/');
    });
    await letRequestsFinish();

    await expectCollected(queryRef);
    tabs.close();
  });

  it('is collected after signing out here', async () => {
    const { user, router, queryRef } = await openTransactions();

    await user.click(screen.getByRole('button', { name: 'Sign out' }));
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/');
    });
    await letRequestsFinish();

    await expectCollected(queryRef);
  });
});

describe('the first screen of the page load after the session ends and the screen is gone', () => {
  it('is collected: its h1 and the rows under it are not kept by the shell', async () => {
    const { user, router } = await openTransactions();
    const headingRef = new WeakRef(screen.getByRole('heading', { level: 1, name: 'Transactions' }));
    const rowRef = new WeakRef(
      screen.getAllByText('ZXP*GLOBALSVCS 0800').at(0) ?? new Error('no row'),
    );

    await user.click(screen.getByRole('button', { name: 'Sign out' }));
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/');
    });
    await letRequestsFinish();

    // The shell stays mounted, as in the browser: only the first screen is gone.
    await collect();
    expect(headingRef.deref()).toBeUndefined();
    expect(rowRef.deref()).toBeUndefined();
  });
});
