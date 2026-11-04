import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';

import type { AppRouter } from '@/app/router';
import { expireSession, isSignedIn, sessionHash } from '@/app/sessionEnd';

import { getGetApiV1BffSessionQueryKey } from './generated/disputedesk';
import type { SessionResponse } from './generated/model';
import { ApiError } from './mutator';
import { isProblem } from './problems';

/** Retry a read once, and only when the bank or the network might recover: 503 or no response at all. */
function shouldRetryRead(failureCount: number, error: Error): boolean {
  const isTemporary =
    error instanceof TypeError || (error instanceof ApiError && error.status === 503);
  return failureCount < 1 && isTemporary;
}

/**
 * The one QueryClient. Its caches handle the two errors that send the person elsewhere, once, for every query and
 * mutation: a 403 `linking-required` goes to /link, and a 401 while signed in ends the session (clears customer data
 * and shows the session-ended screen).
 */
export function createQueryClient(
  getRouter: () => Pick<AppRouter, 'navigate' | 'state'>,
): QueryClient {
  function handleError(error: Error) {
    if (isProblem(error, 'linking-required') && isSignedIn(queryClient)) {
      // The server says the login is not linked, so the SPA's view of it changes too: /link only shows to `unlinked`.
      queryClient.setQueryData<SessionResponse>(getGetApiV1BffSessionQueryKey(), {
        state: 'unlinked',
      });
      void getRouter().navigate({ href: '/link' });
    } else if (error instanceof ApiError && error.status === 401 && isSignedIn(queryClient)) {
      expireSession(queryClient, getRouter());
    }
  }

  const queryClient: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: handleError }),
    mutationCache: new MutationCache({ onError: handleError }),
    defaultOptions: {
      queries: {
        retry: shouldRetryRead,
        // Only a response to a request the person made counts as activity (see useSessionTabs), so neither a tab
        // gaining focus nor the network coming back fetches anything by itself.
        refetchOnWindowFocus: false,
        refetchOnReconnect: false,
        // A customer query runs only while the session is signed in, so once a session ends no screen that is still
        // mounted fetches or fills a query again (TanStack Query, `enabled`). The session query overrides this below.
        enabled: () => isSignedIn(queryClient),
      },
      mutations: { retry: false },
    },
  });

  // A session request answered `anonymous` while the cache said signed in is the server ending the session, as a 401
  // is, so it ends the same way: the signed-in answer goes back into the cache until the session-ended screen is
  // shown (see endSession). A manual write (the clean-up's own `anonymous`, a link) is not an answer.
  let signedInSession: SessionResponse | undefined;
  queryClient.getQueryCache().subscribe((event) => {
    if (event.type !== 'updated' || event.query.queryHash !== sessionHash) return;
    const session = event.query.state.data as SessionResponse | undefined;
    const answeredAnonymous =
      event.action.type === 'success' && !event.action.manual && session?.state === 'anonymous';
    if (signedInSession && answeredAnonymous) {
      queryClient.setQueryData(getGetApiV1BffSessionQueryKey(), signedInSession);
      // One end per window: while the session is already ending, isSignedIn says no.
      if (isSignedIn(queryClient)) expireSession(queryClient, getRouter());
    } else {
      signedInSession = session?.state === 'anonymous' ? undefined : session;
    }
  });

  // The session changes only through a sign-in (a full page load) or an explicit refetch.
  queryClient.setQueryDefaults(getGetApiV1BffSessionQueryKey(), {
    staleTime: Infinity,
    enabled: true,
  });
  return queryClient;
}
