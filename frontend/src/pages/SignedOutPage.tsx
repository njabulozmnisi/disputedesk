import { useSearch } from '@tanstack/react-router';

import { getGetApiV1BffLoginUrl } from '@/api/generated/disputedesk';
import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { Button } from '@/components/ui/button';

/** Shown after any 401 while signed in (EXPERIENCE.md, State Patterns: Session ended). */
export function SignedOutPage() {
  const headingRef = usePageHeading("You've been signed out");
  const { returnUrl } = useSearch({ from: '/signed-out' });

  return (
    <Page width="form">
      <div className="grid gap-4">
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-title font-title tracking-title wrap-break-word text-heading"
        >
          You&apos;ve been signed out
        </h1>
        <p>For your security, we sign you out after 15 minutes without activity.</p>
        <p className="text-muted-foreground">
          Anything you hadn&apos;t submitted wasn&apos;t saved.
        </p>
      </div>
      <div>
        <Button asChild>
          <a href={getGetApiV1BffLoginUrl({ returnUrl })}>Sign in again</a>
        </Button>
      </div>
    </Page>
  );
}
