import { useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';

import { getGetAntiforgeryTokenQueryOptions } from '@/api/generated/disputedesk';
import { ApiError } from '@/api/mutator';

import { leaveToStartPage, postToOtherTabs } from './sessionEnd';

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
 * Sign-out is a top-level form POST (AD-7): the sign-in redirects the browser on to the identity provider. The form
 * carries a request token fetched just before, so it is never stale. Customer data is cleared and the start page shown
 * before the token is fetched, so a failed sign-out leaves nothing in memory (OWASP Session Management Cheat Sheet,
 * Logout). The other tabs are told only once the token is in hand, just before the form is submitted, so they never
 * show a signed-out page while the server session is still live. If the fetch fails nothing is posted or submitted,
 * except on a 401 (the session is already over), which posts `expired`.
 */
export function useSignOut() {
  const queryClient = useQueryClient();
  const router = useRouter();
  const [failed, setFailed] = useState(false);

  async function signOut() {
    setFailed(false);
    await leaveToStartPage(queryClient, router);
    try {
      // queryClient.query goes through the QueryCache, so a 401 here is the server saying the session is over.
      const { requestToken } = await queryClient.query({
        ...getGetAntiforgeryTokenQueryOptions(),
        staleTime: 0,
        gcTime: 0,
      });
      postToOtherTabs(queryClient, 'signed-out');
      submitLogoutForm(requestToken);
    } catch (error) {
      // On a 401 the session is already over: the start page shows with no notice, and the other tabs are told.
      if (error instanceof ApiError && error.status === 401) {
        postToOtherTabs(queryClient, 'expired');
        return;
      }
      setFailed(true);
    }
  }

  return { signOut, failed };
}
