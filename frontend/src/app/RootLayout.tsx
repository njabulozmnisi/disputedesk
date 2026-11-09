import { Outlet, useMatch, useRouterState } from '@tanstack/react-router';
import { useEffect, useRef } from 'react';

import { useGetSession } from '@/api/generated/disputedesk';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';
import { RaiseDraftProvider } from '@/features/disputes/raise/RaiseDraft';
import { cn } from '@/lib/utils';

import { Header } from './Header';
import { InactivityWarning } from './InactivityWarning';
import { LiveRegionProvider } from './LiveRegion';
import { TabBar } from './Navigation';
import { useSessionTabs } from './useSessionTabs';
import { signOutFailedMessage, useSignOut } from './useSignOut';
import { FirstHeadingContext } from './usePageHeading';

function Footer({ wide }: { wide: boolean }) {
  return (
    <footer className="border-t border-border">
      <div
        className={cn(
          'mx-auto grid gap-1 px-4 py-6 text-caption text-muted-foreground md:px-6',
          wide ? 'max-w-(--page-max)' : 'max-w-(--content-max)',
        )}
      >
        <p>Demo data only. Every name, number and amount shown here is made up.</p>
        <p>Your details are used only to show your transactions and handle your disputes.</p>
      </div>
    </footer>
  );
}

// A page the browser restores from the back/forward cache still holds what was in memory when the person left it, so
// it reloads and shows nothing from memory (web.dev, Back/forward cache: update stale or sensitive data).
function useReloadOnPageRestore() {
  useEffect(() => {
    function reloadIfRestored(event: PageTransitionEvent) {
      if (event.persisted) location.reload();
    }
    window.addEventListener('pageshow', reloadIfRestored);
    return () => {
      window.removeEventListener('pageshow', reloadIfRestored);
    };
  }, []);
}

/** The shell around every screen: header (with the skip link), the page, footer, the phone tab bar and the inactivity warning. */
export function RootLayout() {
  // The session-ended screen asks for nothing: any authenticated request would renew the session it reports as ended.
  const onSessionEndedScreen = useMatch({
    from: '/signed-out',
    shouldThrow: false,
    select: (match) => match.search.reason === 'expired',
  });
  const session = useGetSession({ query: { enabled: !onSessionEndedScreen } });
  const state = session.data?.state;
  const isSignedIn = state === 'unlinked' || state === 'linked';
  const pathname = useRouterState({ select: (router) => router.location.pathname });
  const isStartPage = state === 'anonymous' && pathname === '/';
  const { signOut, failed: signOutFailed, clearFailure: clearSignOutFailure } = useSignOut();
  const warningOpen = useSessionTabs(isSignedIn);
  // A sign-out failure belongs to the warning it happened in: the next warning opens without it.
  useEffect(() => {
    if (!warningOpen) clearSignOutFailure();
  }, [warningOpen, clearSignOutFailure]);
  useReloadOnPageRestore();
  const mainRef = useRef<HTMLElement>(null);
  const headingSeen = useRef(false);

  return (
    <LiveRegionProvider>
      <FirstHeadingContext value={headingSeen}>
        <div className={cn('flex flex-1 flex-col', state === 'linked' && 'pb-24 md:pb-0')}>
          <Header
            state={state}
            isStartPage={isStartPage}
            onSignOut={() => void signOut()}
            onSkipToMain={() => mainRef.current?.focus()}
          />
          {signOutFailed ? (
            <div className="mx-auto grid w-full max-w-(--content-max) gap-4 px-4 pt-4 md:px-6">
              <ProblemNotice
                message={signOutFailedMessage}
                nextStep="Try again."
                action={<Button onClick={() => void signOut()}>Sign out</Button>}
              />
            </div>
          ) : null}
          <main id="main" ref={mainRef} tabIndex={-1} className="w-full flex-1 focus:outline-none">
            <RaiseDraftProvider>
              <Outlet />
            </RaiseDraftProvider>
          </main>
          <Footer wide={isStartPage} />
          {state === 'linked' ? <TabBar /> : null}
          {isSignedIn ? (
            <InactivityWarning
              open={warningOpen}
              onSignOut={() => void signOut()}
              signOutFailed={signOutFailed}
            />
          ) : null}
        </div>
      </FirstHeadingContext>
    </LiveRegionProvider>
  );
}
