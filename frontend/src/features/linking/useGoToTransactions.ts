import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';

import { getGetApiV1BffSessionQueryKey } from '@/api/generated/disputedesk';
import type { SessionResponse } from '@/api/generated/model';

/**
 * The login is linked (a 204, or a 403 `already-linked` from another tab). The session cache says so before the
 * navigation, because `SessionGate` would otherwise read `unlinked` and send the customer back to /link.
 */
export function useGoToTransactions() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  return () => {
    queryClient.setQueryData<SessionResponse>(getGetApiV1BffSessionQueryKey(), {
      state: 'linked',
    });
    void navigate({ to: '/transactions', replace: true });
  };
}
