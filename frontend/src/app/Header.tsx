import { Link } from '@tanstack/react-router';

import type { SessionState } from '@/api/generated/model';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

import { TopNav } from './Navigation';

type HeaderProps = {
  /** Undefined until the session request answers; the header then shows only the wordmark. */
  state: SessionState | undefined;
  /** The start page alone is wider than the rest and offers Sign in (DESIGN.md, Layout & Spacing). */
  isStartPage: boolean;
  onSignOut: () => void;
  /** Moves focus to the main region; the skip link below calls it. */
  onSkipToMain: () => void;
};

/** One header on every screen (DESIGN.md, Layout & Spacing). What it shows depends on the authorisation state. */
export function Header({ state, isStartPage, onSignOut, onSkipToMain }: HeaderProps) {
  const isSignedIn = state === 'unlinked' || state === 'linked';

  return (
    <header className="border-b border-border bg-card">
      <div
        className={cn(
          'mx-auto flex min-h-16 w-full items-center gap-6 px-4 py-2 md:px-6',
          isStartPage ? 'max-w-(--page-max)' : 'max-w-(--content-max)',
        )}
      >
        {/* The first focusable element, hidden until focused (WCAG 2.2 SC 2.4.1, 2.4.7; technique G1). Focused, it floats
          out of the flow, so nothing moves and a click made while it is focused still lands (SC 2.5.2, 3.2.1). With no
          offsets, an absolutely positioned child of this flex container sits where it would as the only item: at the
          container's padding edge and centred like the home link. It is as tall as the home link (--tap-target) and
          wider, so it fully covers the home link and no 44px target is left partly covered (SC 2.5.8). Focus moves by
          script, with no hash navigation for the router to see. */}
        <a
          href="#main"
          className="sr-only focus:not-sr-only focus:absolute focus:z-20 focus:inline-flex focus:min-h-(--tap-target) focus:items-center focus:rounded-full focus:bg-card focus:px-4 focus:shadow-overlay"
          onClick={(event) => {
            event.preventDefault();
            onSkipToMain();
          }}
        >
          Skip to main content
        </a>
        <Link
          to="/"
          className="inline-flex min-h-(--tap-target) items-center text-subheading font-title tracking-title text-heading no-underline hover:text-heading"
        >
          {/* The dot follows the name like a full stop; ml-0.5 (2px) is DESIGN.md's one exception to the 4-based spacing. */}
          <span className="inline-flex items-baseline">
            DisputeDesk
            <span aria-hidden="true" className="ml-0.5 size-2 shrink-0 rounded-full bg-primary" />
          </span>
        </Link>
        {state === 'linked' ? <TopNav /> : null}
        {isSignedIn ? (
          <button
            type="button"
            onClick={onSignOut}
            className="ml-auto inline-flex min-h-(--tap-target) cursor-pointer items-center rounded-full px-3 font-strong text-primary hover:text-primary-hover hover:underline"
          >
            Sign out
          </button>
        ) : null}
        {isStartPage ? (
          <Button asChild variant="secondary" size="compact" className="ml-auto">
            <a href="/api/v1/authentication/login">Sign in</a>
          </Button>
        ) : null}
      </div>
    </header>
  );
}
