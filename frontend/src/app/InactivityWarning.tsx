import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { getGetApiV1BffSessionQueryOptions } from '@/api/generated/disputedesk';
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

import { signOutFailedMessage } from './useSignOut';

const unexpectedErrorMessage = 'Something went wrong on our side. Try again.';

type InactivityWarningProps = {
  open: boolean;
  onSignOut: () => void;
  /** The sign-out failed: the modal dialog hides the notice behind it, so the failure shows here too. */
  signOutFailed: boolean;
};

/**
 * Mounted while a person is signed in. It presents the warning that `useSessionTabs` opens, 2 minutes before the idle
 * limit (OWASP Session Management, idle timeout).
 */
export function InactivityWarning({ open, onSignOut, signOutFailed }: InactivityWarningProps) {
  const queryClient = useQueryClient();
  const [renewFailed, setRenewFailed] = useState(false);

  // The renewal is a fresh read of the session through the query cache. Its response moves this tab's deadline and
  // closes the dialog (`useSessionTabs`); an `anonymous` answer or a 401 ends the session (`queryClient`).
  // Any other failure is shown here.
  async function staySignedIn() {
    setRenewFailed(false);
    try {
      // No retry: one request renews the session, as the person pressed the button once.
      await queryClient.query({
        ...getGetApiV1BffSessionQueryOptions(),
        staleTime: 0,
        retry: false,
      });
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 401)) setRenewFailed(true);
    }
  }

  return (
    // Escape acts as "Stay signed in" (WAI-ARIA APG alertdialog pattern). The dialog is controlled, so it closes only
    // when a renewal succeeds: `useSessionTabs` sets `open` to false on a successful response.
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
        {signOutFailed ? (
          <p role="alert" className="text-destructive font-strong">
            {signOutFailedMessage} Try again.
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
