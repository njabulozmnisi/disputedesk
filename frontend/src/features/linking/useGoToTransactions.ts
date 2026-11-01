import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';

import { getGetSessionQueryKey } from '@/api/generated/disputedesk';
import type { SessionResponse } from '@/api/generated/model';
import { isSignedIn } from '@/app/sessionEnd';

/**
 * The login is linked (a 204, or a 403 `already-linked` from another tab). The session cache says so before the
 * navigation, because `SessionGate` would otherwise read `unlinked` and send the customer back to /link.
 */
export function useGoToTransactions() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  return () => {
    // The session ended while the request was in flight: it must not come back as linked.
    if (!isSignedIn(queryClient)) return;
    queryClient.setQueryData<SessionResponse>(getGetSessionQueryKey(), {
      state: 'linked',
    });
    void navigate({ to: '/transactions', replace: true });
  };
}
