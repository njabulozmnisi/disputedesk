import { useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';

import { getApiV1BffSession, getGetApiV1BffSessionQueryKey } from '@/api/generated/disputedesk';
import { ApiError } from '@/api/mutator';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';

import { expireSession, isSignedIn } from './sessionEnd';

const unexpectedErrorMessage = 'Something went wrong on our side. Try again.';

type InactivityWarningProps = {
  open: boolean;
  setOpen: (open: boolean) => void;
  onSignOut: () => void;
};

/**
 * Mounted while a person is signed in. It presents the warning that `useSessionTabs` opens, 2 minutes before the idle
 * limit (OWASP Session Management, idle timeout).
 */
export function InactivityWarning({ open, setOpen, onSignOut }: InactivityWarningProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [renewFailed, setRenewFailed] = useState(false);

  // The session is asked for outside the query cache, so that an `anonymous` answer reaches the cache only through
  // `expireSession`, after the session-ended screen is shown (see endSession).
  async function staySignedIn() {
    setRenewFailed(false);
    try {
      const renewed = await getApiV1BffSession();
      // The session ended while the renewal was in flight: its late answer restores nothing.
      if (!isSignedIn(queryClient)) return;
      if (renewed.state === 'anonymous') {
        // The session had already ended: the server answers the session request as anonymous, not with a 401.
        expireSession(queryClient, router);
        return;
      }
      queryClient.setQueryData(getGetApiV1BffSessionQueryKey(), renewed);
      setOpen(false);
    } catch (error) {
      if (!isSignedIn(queryClient)) return;
      if (error instanceof ApiError && error.status === 401) expireSession(queryClient, router);
      else setRenewFailed(true);
    }
  }

  return (
    // Escape acts as "Stay signed in" (WAI-ARIA APG alertdialog pattern). The dialog is controlled, so it closes only
    // when staySignedIn succeeds and sets `open` to false.
    <AlertDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) void staySignedIn();
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>You&apos;ll be signed out soon.</AlertDialogTitle>
          <AlertDialogDescription>
            For your security, we sign you out after 15 minutes without activity.
          </AlertDialogDescription>
        </AlertDialogHeader>
        {renewFailed ? (
          <p role="alert" className="text-destructive font-strong">
            {unexpectedErrorMessage}
          </p>
        ) : null}
        <AlertDialogFooter>
          <AlertDialogAction
            onClick={(event) => {
              // Radix closes the dialog on click; it closes only once the session is renewed.
              event.preventDefault();
              void staySignedIn();
            }}
          >
            Stay signed in
          </AlertDialogAction>
          <Button variant="secondary" onClick={onSignOut}>
            Sign out
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
