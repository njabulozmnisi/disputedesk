import { Outlet, useRouterState } from '@tanstack/react-router';
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
import { useSignOut } from './useSignOut';
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

/** The shell around every screen: skip link, header, the page, footer, the phone tab bar and the inactivity warning. */
export function RootLayout() {
  const session = useGetSession();
  const state = session.data?.state;
  const isSignedIn = state === 'unlinked' || state === 'linked';
  const pathname = useRouterState({ select: (router) => router.location.pathname });
  const isStartPage = state === 'anonymous' && pathname === '/';
  const { signOut, failed: signOutFailed } = useSignOut();
  const { warningOpen, setWarningOpen } = useSessionTabs(isSignedIn);
  useReloadOnPageRestore();
  const mainRef = useRef<HTMLElement>(null);
  const headingSeen = useRef(false);

  return (
    <LiveRegionProvider>
      <FirstHeadingContext value={headingSeen}>
        <div className={cn('flex flex-1 flex-col', state === 'linked' && 'pb-24 md:pb-0')}>
          {/* The first focusable element, hidden until focused (WCAG 2.2 SC 2.4.1, 2.4.7; technique G1). Focus moves by
            script, with no hash navigation for the router to see. */}
          <a
            href="#main"
            className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-20 focus:rounded-full focus:bg-card focus:px-4 focus:py-2 focus:shadow-overlay"
            onClick={(event) => {
              event.preventDefault();
              mainRef.current?.focus();
            }}
          >
            Skip to main content
          </a>
          <Header state={state} wide={isStartPage} onSignOut={() => void signOut()} />
          {signOutFailed ? (
            <div className="mx-auto grid w-full max-w-(--content-max) gap-4 px-4 pt-4 md:px-6">
              <ProblemNotice
                message="We couldn't finish signing you out."
                nextStep="Try again."
                action={<Button onClick={() => void signOut()}>Sign out</Button>}
              />
            </div>
          ) : null}
          <main id="main" ref={mainRef} tabIndex={-1} className="w-full flex-1 focus:outline-none">
            <RaiseDraftProvider signedIn={isSignedIn}>
              <Outlet />
            </RaiseDraftProvider>
          </main>
          <Footer wide={isStartPage} />
          {state === 'linked' ? <TabBar /> : null}
          {isSignedIn ? (
            <InactivityWarning
              open={warningOpen}
              setOpen={setWarningOpen}
              onSignOut={() => void signOut()}
            />
          ) : null}
        </div>
      </FirstHeadingContext>
    </LiveRegionProvider>
  );
}
