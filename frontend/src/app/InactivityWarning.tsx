import { useRouter } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { useGetApiV1BffSession } from '@/api/generated/disputedesk';
import { onApiResponse } from '@/api/mutator';
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

// The server signs a person out after 15 minutes without an authenticated request (AD-7). The warning appears after
// 13, which leaves 2 minutes: well over the 20 seconds WCAG 2.2 SC 2.2.1 (Timing Adjustable) asks for.
const warnAfterIdleMs = 13 * 60 * 1000;

const unexpectedErrorMessage = 'Something went wrong on our side. Try again.';

type InactivityWarningProps = {
  onSignOut: () => void;
  signOutFailed: boolean;
};

/**
 * Mounted while a person is signed in. It only presents the warning: the server enforces the idle limit
 * (OWASP Session Management, idle timeout).
 */
export function InactivityWarning({ onSignOut, signOutFailed }: InactivityWarningProps) {
  const router = useRouter();
  const session = useGetApiV1BffSession();
  const [open, setOpen] = useState(false);
  const [renewFailed, setRenewFailed] = useState(false);

  // The server renews the idle limit on every authenticated request, so every API response except a 401 restarts the
  // timer. A 401 means the session already ended: the QueryClient sends the person to the session-ended screen.
  // shortcut: per-tab idle timer, another tab's activity can only make the warning early, never late; share it with BroadcastChannel if early warnings confuse people
  useEffect(() => {
    let timer = setTimeout(() => {
      setOpen(true);
    }, warnAfterIdleMs);

    const stopListening = onApiResponse((status) => {
      if (status === 401) return;
      clearTimeout(timer);
      timer = setTimeout(() => {
        setOpen(true);
      }, warnAfterIdleMs);
    });

    return () => {
      clearTimeout(timer);
      stopListening();
    };
  }, []);

  async function staySignedIn() {
    setRenewFailed(false);
    // A 401 reaches the success branch below: the QueryClient's 401 handler writes `anonymous` into the session
    // cache before `refetch()` resolves, so the result is a success whose data says `anonymous`.
    const result = await session.refetch();
    if (!result.isSuccess) {
      setRenewFailed(true);
      return;
    }
    if (result.data.state === 'anonymous') {
      // The session had already ended: the server answers the session request as anonymous, not with a 401. After a
      // 401 the QueryClient has already sent the person there (and set the cache to anonymous), so this must not
      // replace the route it carried.
      if (router.state.location.pathname === '/signed-out') return;
      void router.navigate({
        to: '/signed-out',
        search: { reason: 'expired', returnUrl: router.state.location.href },
      });
      return;
    }
    setOpen(false);
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
        {renewFailed || signOutFailed ? (
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
