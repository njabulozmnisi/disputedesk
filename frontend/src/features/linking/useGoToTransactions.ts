import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';

import { getGetApiV1BffSessionQueryKey } from '@/api/generated/disputedesk';
import type { SessionResponse } from '@/api/generated/model';
import { isSignedIn } from '@/app/sessionEnd';

/**
 * The login is linked (a 204, or a 403 `already-linked` from another tab). The session cache says so before the
 * navigation, because the `_linked` layout route's `beforeLoad` would otherwise read `unlinked` and send the customer
 * back to /link.
 */
export function useGoToTransactions() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  return () => {
    // The session ended while the request was in flight: it must not come back as linked.
    if (!isSignedIn(queryClient)) return;
    queryClient.setQueryData<SessionResponse>(getGetApiV1BffSessionQueryKey(), {
      state: 'linked',
    });
    void navigate({ to: '/transactions', replace: true });
  };
}
