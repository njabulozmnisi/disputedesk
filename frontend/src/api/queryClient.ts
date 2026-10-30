import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';

import type { AppRouter } from '@/app/router';

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
 * The one QueryClient. Its caches handle the two errors that send the person elsewhere, once, for every query and
 * mutation: a 403 `linking-required` goes to /link, and a 401 while signed in goes to the session-ended screen.
 */
export function createQueryClient(router: Pick<AppRouter, 'navigate' | 'state'>): QueryClient {
  function isSignedIn(): boolean {
    const session = queryClient.getQueryData<SessionResponse>(getGetApiV1BffSessionQueryKey());
    return session?.state === 'unlinked' || session?.state === 'linked';
  }

  function handleError(error: Error) {
    if (isProblem(error, 'linking-required')) {
      // The server says the login is not linked, so the SPA's view of it changes too: /link only shows to `unlinked`.
      queryClient.setQueryData<SessionResponse>(getGetApiV1BffSessionQueryKey(), {
        state: 'unlinked',
      });
      void router.navigate({ href: '/link' });
    } else if (error instanceof ApiError && error.status === 401 && isSignedIn()) {
      // The server has ended the session, so the SPA's view of it ends too.
      queryClient.setQueryData<SessionResponse>(getGetApiV1BffSessionQueryKey(), {
        state: 'anonymous',
      });
      void router.navigate({
        to: '/signed-out',
        search: { reason: 'expired', returnUrl: returnUrlAfterSignIn(router.state.location) },
      });
    }
  }

  const queryClient = new QueryClient({
    queryCache: new QueryCache({ onError: handleError }),
    mutationCache: new MutationCache({ onError: handleError }),
    defaultOptions: {
      queries: {
        retry: shouldRetryRead,
        // Only a response to a request the person made counts as activity (see InactivityWarning), so neither a tab
        // gaining focus nor the network coming back fetches anything by itself.
        refetchOnWindowFocus: false,
        refetchOnReconnect: false,
      },
      mutations: { retry: false },
    },
  });

  // The session changes only through a sign-in (a full page load) or an explicit refetch.
  queryClient.setQueryDefaults(getGetApiV1BffSessionQueryKey(), { staleTime: Infinity });
  return queryClient;
}
