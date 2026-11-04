import { hashKey, type QueryClient } from '@tanstack/react-query';

import { getGetApiV1BffSessionQueryKey } from '@/api/generated/disputedesk';
import type { SessionResponse } from '@/api/generated/model';

import type { AppRouter } from './router';

/** The only messages tabs send each other: best-effort notices with no customer data (HTML Living Standard, BroadcastChannel). */
export type TabMessage = 'activity' | 'expired' | 'signed-out';
export const sessionChannelName = 'disputedesk-session';

export const sessionHash = hashKey(getGetApiV1BffSessionQueryKey());
const isCustomerData = (query: { queryHash: string }) => query.queryHash !== sessionHash;

// Clients whose session is ending: the screen is being shown but the clean-up has not run yet (see endSession).
const endingSessions = new WeakSet<QueryClient>();

/** True while the cache says signed in and the session is not ending: a late response checks this before it writes. */
export function isSignedIn(queryClient: QueryClient): boolean {
  if (endingSessions.has(queryClient)) return false;
  const session = queryClient.getQueryData<SessionResponse>(getGetApiV1BffSessionQueryKey());
  return session?.state === 'unlinked' || session?.state === 'linked';
}

/**
 * Empties every customer query in place, without the network (OWASP ASVS 5.0.0 V14.3.1): outstanding customer queries
 * are cancelled and reset to their empty state, and the mutation cache is emptied. The queries stay in the cache, so a
 * screen still observing one never holds a query the cache has dropped; removeCustomerQueries removes them once the
 * screen has changed. A reset refetches only enabled queries, and none is enabled once the session is not signed in
 * (see createQueryClient, `enabled`).
 */
function resetCustomerData(queryClient: QueryClient) {
  void queryClient.resetQueries({ predicate: isCustomerData });
  queryClient.getMutationCache().clear();
}

/**
 * Removes the (already empty) customer queries from the cache. Call it after the navigation away from the customer's
 * screens has landed. React unmounts a screen a moment after the navigation resolves, and a query removed while a
 * screen still observes it is left with a garbage-collection timer that nothing can clear (TanStack Query,
 * `Query.removeObserver` schedules it), so each query is removed when its last observer has gone.
 */
export function removeCustomerQueries(queryClient: QueryClient) {
  const cache = queryClient.getQueryCache();
  function removeUnobserved() {
    const left = cache.findAll({ predicate: isCustomerData });
    left
      .filter((query) => query.getObserversCount() === 0)
      .forEach((query) => {
        cache.remove(query);
      });
    if (left.every((query) => query.getObserversCount() === 0)) unsubscribe();
  }
  const unsubscribe = cache.subscribe((event) => {
    if (event.type === 'observerRemoved') removeUnobserved();
  });
  removeUnobserved();
}

/**
 * Clears all customer data from memory, without the network: every query is cancelled, the customer queries are reset,
 * the mutation cache is emptied and the session says `anonymous`. The raise draft clears itself when the session stops
 * being signed in (RaiseDraftProvider, `signedIn`). The caller navigates, then calls removeCustomerQueries. Running it
 * twice leaves the same state.
 */
export function clearCustomerData(queryClient: QueryClient) {
  // Every query is cancelled first, the session's too, so an answer that lands later is ignored and cannot write
  // `linked` back over `anonymous`.
  void queryClient.cancelQueries();
  queryClient.setQueryData<SessionResponse>(getGetApiV1BffSessionQueryKey(), {
    state: 'anonymous',
  });
  resetCustomerData(queryClient);
}

/** Sign-out here or in another tab: clear, navigate to the start page, then remove the customer queries. */
export async function leaveToStartPage(
  queryClient: QueryClient,
  router: Pick<AppRouter, 'navigate'>,
) {
  clearCustomerData(queryClient);
  try {
    await router.navigate({ to: '/' });
  } finally {
    removeCustomerQueries(queryClient);
  }
}

/**
 * "Sign in again" returns to the route the person was on, except from a raise or dispute detail route: its path
 * holds a transaction or dispute ID, which must not reach the BFF login URL, so it returns to the list (AD-31). The
 * router matches paths case-insensitively, so this compare does too.
 */
function returnUrlAfterSignIn(location: { pathname: string; href: string }): string {
  const pathname = location.pathname.toLowerCase();
  if (pathname.startsWith('/transactions/')) return '/transactions';
  if (pathname.startsWith('/disputes/')) return '/disputes';
  return location.href;
}

/**
 * Clears the customer data, shows the session-ended screen, then marks the session `anonymous`. The idle limit, a 401
 * and another tab's `expired` end the session this way. The session stays signed in until the screen is shown, because
 * the `anonymous` state makes a layout route's `beforeLoad` send a navigation to the start page, and that redirect
 * would win over this one. The cached query and mutation data is already empty while the screen changes, and the
 * queries are removed after it.
 */
export async function endSession(
  queryClient: QueryClient,
  router: Pick<AppRouter, 'navigate' | 'state'>,
) {
  // All customer data is emptied first, so none is in memory while the screen changes and a response that arrives
  // meanwhile brings nothing back. Only the session query stays, still signed in, so that a layout route's `beforeLoad`
  // does not race the screen. The queries are removed once the screen has changed.
  endingSessions.add(queryClient);
  resetCustomerData(queryClient);
  try {
    await router.navigate({
      to: '/signed-out',
      search: { reason: 'expired', returnUrl: returnUrlAfterSignIn(router.state.location) },
    });
  } finally {
    clearCustomerData(queryClient);
    endingSessions.delete(queryClient);
    removeCustomerQueries(queryClient);
  }
}

// The channel useSessionTabs opened for each app instance, so that code outside React can post on it.
const channels = new WeakMap<QueryClient, BroadcastChannel>();

export function registerChannel(queryClient: QueryClient, channel: BroadcastChannel | undefined) {
  if (channel) channels.set(queryClient, channel);
  else channels.delete(queryClient);
}

export function postToOtherTabs(queryClient: QueryClient, message: TabMessage) {
  channels.get(queryClient)?.postMessage(message);
}

/** The server said the session is over (a 401, or a session request answered `anonymous`): tell the other tabs too. */
export function expireSession(
  queryClient: QueryClient,
  router: Pick<AppRouter, 'navigate' | 'state'>,
) {
  void endSession(queryClient, router);
  postToOtherTabs(queryClient, 'expired');
}
