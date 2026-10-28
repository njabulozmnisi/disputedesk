import { Link } from '@tanstack/react-router';

import type { SessionState } from '@/api/generated/model';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

import { TopNav } from './Navigation';

type HeaderProps = {
  /** Undefined until the session request answers; the header then shows only the wordmark. */
  state: SessionState | undefined;
  /** The start page alone is wider than the rest (DESIGN.md, Layout & Spacing). */
  wide: boolean;
  onSignOut: () => void;
};

/** One header on every screen (DESIGN.md, Layout & Spacing). What it shows depends on the authorisation state. */
export function Header({ state, wide, onSignOut }: HeaderProps) {
  const isSignedIn = state === 'unlinked' || state === 'linked';

  return (
    <header className="border-b border-border bg-card">
      <div
        className={cn(
          'mx-auto flex min-h-16 w-full items-center gap-6 px-4 py-2 md:px-6',
          wide ? 'max-w-(--page-max)' : 'max-w-(--content-max)',
        )}
      >
        <Link
          to="/"
          className="inline-flex items-center gap-2 text-subheading font-title tracking-title text-heading no-underline hover:text-heading"
        >
          <span aria-hidden="true" className="size-6 shrink-0 rounded-full bg-primary" />
          DisputeDesk
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
        {wide ? (
          <Button asChild variant="secondary" size="compact" className="ml-auto">
            <a href="/api/v1/bff/login">Sign in</a>
          </Button>
        ) : null}
      </div>
    </header>
  );
}
