import {
  focusManager,
  hashKey,
  MutationCache,
  QueryCache,
  QueryClient,
} from '@tanstack/react-query';

import type { AppRouter } from '@/app/router';
import { leaveForSessionEnded } from '@/app/sessionEnd';

import { getGetSessionQueryKey } from './generated/disputedesk';
import type { SessionResponse } from './generated/model';
import { ApiError } from './mutator';
import { isProblem } from './problems';

/** Retry a read once, and only when the bank or the network might recover: 503 or no response at all. */
function shouldRetryRead(failureCount: number, error: Error): boolean {
  const isTemporary =
    error instanceof TypeError || (error instanceof ApiError && error.status === 503);
  return failureCount < 1 && isTemporary;
}

// TanStack Query's default focus listener hears `visibilitychange` only, so a visible window that gains focus would not
// check the session. This is its documented `setEventListener` with `focus` added (TanStack Query, focusManager).
focusManager.setEventListener((handleFocus) => {
  const listener = () => {
    handleFocus();
  };
  window.addEventListener('visibilitychange', listener, false);
  window.addEventListener('focus', listener, false);
  return () => {
    window.removeEventListener('visibilitychange', listener);
    window.removeEventListener('focus', listener);
  };
});

/**
 * The one QueryClient. Its caches handle the two errors that send the person elsewhere, once, for every query and
 * mutation: a 403 `linking-required` goes to /link, and a 401 ends the session (the browser leaves for the
 * session-ended screen). A session answer of `anonymous` after a signed-in one ends it too.
 */
export function createQueryClient(getRouter: () => Pick<AppRouter, 'navigate'>): QueryClient {
  function handleError(error: Error) {
    if (isProblem(error, 'linking-required')) {
      // The server says the login is not linked, so the SPA's view of it changes too: /link only shows to `unlinked`.
      queryClient.setQueryData<SessionResponse>(getGetSessionQueryKey(), {
        state: 'unlinked',
      });
      void getRouter().navigate({ href: '/link' });
    } else if (error instanceof ApiError && error.status === 401) {
      leaveForSessionEnded();
    }
  }

  // Whether the last session answer fetched from the server was signed in. `onSuccess` runs for fetched data only,
  // never for `setQueryData`, so the app's own change to `unlinked` is not an answer (TanStack Query, QueryCache).
  let signedIn = false;

  function handleSuccess(_data: unknown, query: { queryHash: string }) {
    if (query.queryHash !== hashKey(getGetSessionQueryKey())) return;
    // The cache holds the typed response; `onSuccess` hands over only the untyped data and the query's hash.
    const state = queryClient.getQueryData<SessionResponse>(getGetSessionQueryKey())?.state;
    const nowSignedIn = state === 'unlinked' || state === 'linked';
    if (signedIn && !nowSignedIn) leaveForSessionEnded();
    signedIn = nowSignedIn;
  }

  const queryClient: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: handleError, onSuccess: handleSuccess }),
    mutationCache: new MutationCache({ onError: handleError }),
    defaultOptions: {
      queries: {
        retry: shouldRetryRead,
        // Only a response to a request the person made counts as activity (see useSessionTabs), so neither a tab
        // gaining focus nor the network coming back fetches anything by itself; the session query is the exception.
        refetchOnWindowFocus: false,
        refetchOnReconnect: false,
      },
      mutations: { retry: false },
    },
  });

  // The session changes through a sign-in (a full page load), an explicit refetch, or a tab being brought to the
  // front while signed in: that is how a tab learns that the session ended elsewhere. Nobody signed in has nothing to
  // learn, and the start page must not switch over another tab's sign-in. `'always'` because `true` refetches only
  // stale data, and this query is never stale (TanStack Query, Window Focus Refetching; `refetchOnWindowFocus` as a
  // function).
  queryClient.setQueryDefaults(getGetSessionQueryKey(), {
    staleTime: Infinity,
    refetchOnWindowFocus: () => (signedIn ? 'always' : false),
  });
  return queryClient;
}
