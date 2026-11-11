import { Link } from '@tanstack/react-router';

/**
 * The same block, in the same place, on every linking screen and state (WCAG 2.2 SC 3.2.6 Consistent Help): a region
 * named by its h2, after a hairline. Only the code screens offer "Start again", because only they follow a first step.
 */
export function ContactHelp({ offerStartAgain = false }: { offerStartAgain?: boolean }) {
  return (
    <section aria-labelledby="help-heading" className="grid gap-2 border-t border-border pt-6">
      <h2 id="help-heading" className="text-body font-strong">
        Need help?
      </h2>
      {offerStartAgain ? (
        // An inline link in a sentence, so it keeps the text's line height (about 22px) and not the 44px of a
        // standalone control: WCAG 2.2 SC 2.5.8 exempts a target inside a sentence (the "Inline" exception).
        <p className="text-muted-foreground">
          Didn&apos;t get a code? <Link to="/link">Start again</Link>.
        </p>
      ) : null}
      <p className="text-muted-foreground">Can&apos;t link online? Contact the bank.</p>
    </section>
  );
}
