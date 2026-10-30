import { Outlet, useRouterState } from '@tanstack/react-router';
import { useRef } from 'react';

import { useGetApiV1BffSession } from '@/api/generated/disputedesk';
import { ProblemNotice } from '@/components/ProblemNotice';
import { cn } from '@/lib/utils';

import { Header } from './Header';
import { InactivityWarning } from './InactivityWarning';
import { LiveRegionProvider } from './LiveRegion';
import { TabBar } from './Navigation';
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

/** The shell around every screen: skip link, header, the page, footer, the phone tab bar and the inactivity warning. */
export function RootLayout() {
  const session = useGetApiV1BffSession();
  const state = session.data?.state;
  const isSignedIn = state === 'unlinked' || state === 'linked';
  const pathname = useRouterState({ select: (router) => router.location.pathname });
  const isStartPage = state === 'anonymous' && pathname === '/';
  const { signOut, failed: signOutFailed } = useSignOut();
  const mainRef = useRef<HTMLElement>(null);
  const firstHeading = useRef<HTMLHeadingElement>(null);

  return (
    <LiveRegionProvider>
      <FirstHeadingContext value={firstHeading}>
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
          {isSignedIn && signOutFailed ? (
            <div className="mx-auto w-full max-w-(--content-max) px-4 pt-4 md:px-6">
              <ProblemNotice message="Something went wrong on our side. Try again." />
            </div>
          ) : null}
          <main id="main" ref={mainRef} tabIndex={-1} className="w-full flex-1 focus:outline-none">
            <Outlet />
          </main>
          <Footer wide={isStartPage} />
          {state === 'linked' ? <TabBar /> : null}
          {isSignedIn ? (
            <InactivityWarning onSignOut={() => void signOut()} signOutFailed={signOutFailed} />
          ) : null}
        </div>
      </FirstHeadingContext>
    </LiveRegionProvider>
  );
}
