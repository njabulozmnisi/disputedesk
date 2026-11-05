import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { getGetAntiforgeryTokenQueryOptions } from '@/api/generated/disputedesk';
import { ApiError } from '@/api/mutator';

export const signOutFailedMessage = "We couldn't finish signing you out.";

function submitLogoutForm(requestToken: string) {
  const form = document.createElement('form');
  form.method = 'post';
  form.action = '/api/v1/authentication/logout';

  const tokenField = document.createElement('input');
  tokenField.type = 'hidden';
  tokenField.name = '__RequestVerificationToken';
  tokenField.value = requestToken;

  form.append(tokenField);
  document.body.append(form);
  form.submit();
}

/**
 * Sign-out is a top-level form POST (AD-7): the sign-in redirects the browser on to the identity provider, and the
 * document navigation drops everything in memory. The form carries a request token fetched just before, so it is never
 * stale. If the fetch fails, the server session is still live: the screen keeps its data and `failed` shows the notice
 * above it. A 401 on the fetch means the session is already over, and the query cache leaves the document
 * (`queryClient`).
 */
export function useSignOut() {
  const queryClient = useQueryClient();
  const [failed, setFailed] = useState(false);

  async function signOut() {
    setFailed(false);
    try {
      // queryClient.query goes through the QueryCache, so a 401 here is the server saying the session is over.
      const { requestToken } = await queryClient.query({
        ...getGetAntiforgeryTokenQueryOptions(),
        staleTime: 0,
        gcTime: 0,
      });
      submitLogoutForm(requestToken);
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 401)) setFailed(true);
    }
  }

  return { signOut, failed };
}
