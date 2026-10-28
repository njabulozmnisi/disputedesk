import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { getGetApiV1BffAntiforgeryQueryOptions } from '@/api/generated/disputedesk';

function submitLogoutForm(requestToken: string) {
  const form = document.createElement('form');
  form.method = 'post';
  form.action = '/api/v1/bff/logout';

  const tokenField = document.createElement('input');
  tokenField.type = 'hidden';
  tokenField.name = '__RequestVerificationToken';
  tokenField.value = requestToken;

  form.append(tokenField);
  document.body.append(form);
  form.submit();
}

/**
 * Sign-out is a top-level form POST (AD-7): the BFF redirects the browser on to the identity provider. The form
 * carries a request token fetched just before, so it is never stale. If the fetch fails nothing is submitted.
 */
export function useSignOut() {
  const queryClient = useQueryClient();
  const [failed, setFailed] = useState(false);

  async function signOut() {
    setFailed(false);
    try {
      // queryClient.query goes through the QueryCache, so a 401 here takes the person to the session-ended screen.
      const { requestToken } = await queryClient.query({
        ...getGetApiV1BffAntiforgeryQueryOptions(),
        staleTime: 0,
        gcTime: 0,
      });
      submitLogoutForm(requestToken);
    } catch {
      setFailed(true);
    }
  }

  return { signOut, failed };
}
