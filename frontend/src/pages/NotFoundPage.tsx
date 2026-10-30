import { Link } from '@tanstack/react-router';

import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { Button } from '@/components/ui/button';

/** The not-found route for every state: an unknown path or dispute (docs/ux/mockups/not-found.html). */
export function NotFoundPage() {
  const headingRef = usePageHeading("We can't find that dispute");

  return (
    <Page width="form">
      <div className="grid gap-4">
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-title font-title tracking-title wrap-break-word text-heading"
        >
          We can&apos;t find that dispute
        </h1>
        <p className="text-muted-foreground">Check the link, or find it in your disputes.</p>
      </div>
      <div>
        <Button asChild>
          <Link to="/disputes">Go to your disputes</Link>
        </Button>
      </div>
    </Page>
  );
}
