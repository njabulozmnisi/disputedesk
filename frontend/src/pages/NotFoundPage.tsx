import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { Button } from '@/components/ui/button';

/**
 * The not-found route for every state: an unknown path, and a link to a screen whose story has not landed yet
 * (docs/ux/mockups/not-found.html).
 */
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
        {/* shortcut: a plain link (a full page load) until the disputes story adds its route */}
        <Button asChild>
          <a href="/disputes">Go to your disputes</a>
        </Button>
      </div>
    </Page>
  );
}
