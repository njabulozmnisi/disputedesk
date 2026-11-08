import { useSearch } from '@tanstack/react-router';
import { Clock } from 'lucide-react';

import { getLoginUrl } from '@/api/generated/disputedesk';
import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { StateSurface } from '@/components/StateSurface';
import { Button } from '@/components/ui/button';

/** Shown after any 401 while signed in (EXPERIENCE.md, State Patterns: Session ended). */
export function SignedOutPage() {
  const headingRef = usePageHeading("You've been signed out");
  const { returnUrl } = useSearch({ from: '/signed-out' });

  return (
    <Page width="form">
      <StateSurface
        icon={Clock}
        heading={
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="text-title font-title tracking-title wrap-break-word text-heading"
          >
            You&apos;ve been signed out
          </h1>
        }
      >
        <p>For your security, we sign you out after 15 minutes without activity.</p>
        <p className="text-muted-foreground">
          Anything you hadn&apos;t submitted wasn&apos;t saved.
        </p>
        <div className="mt-3">
          <Button asChild>
            <a href={getLoginUrl({ returnUrl })}>Sign in again</a>
          </Button>
        </div>
      </StateSurface>
    </Page>
  );
}
