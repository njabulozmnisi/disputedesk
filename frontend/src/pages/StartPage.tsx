import { usePageHeading } from '@/app/usePageHeading';
import { Button } from '@/components/ui/button';

// Sign-in and sign-up are hosted by the identity provider: the SPA only navigates to the BFF (AD-7). The BFF has one
// entry point, and the hosted page offers "create a login".
const signInUrl = '/api/v1/bff/login';

const steps = [
  {
    title: 'Link your login',
    text: "Create a login, then prove you're a customer of the bank with your ID number, account number and a one-time code sent to your cellphone.",
  },
  {
    title: 'Pick a card transaction',
    text: "Choose a card transaction from the last 90 days, say what's wrong and add a note if it helps.",
  },
  {
    title: 'Follow its status',
    text: "Each change the bank makes shows on your dispute's timeline, with any note meant for you.",
  },
];

/** A sample dispute card, built from the same tokens as the real one (DESIGN.md, Start page). Never read aloud. */
function SampleDispute() {
  return (
    <div aria-hidden="true" className="grid gap-4 rounded-lg bg-card p-6 shadow-overlay">
      <div>
        <p className="font-strong">ZXP*GLOBALSVCS 0800</p>
        <p className="text-caption text-muted-foreground">Unrecognised, card ending 3456</p>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
        <p className="text-amount-hero font-title text-heading tabular-nums">R849.00</p>
        <span className="inline-flex items-center gap-1 rounded-full bg-status-info-bg px-2.5 py-0.5 text-label font-strong text-status-info-fg">
          <span aria-hidden="true">◔</span>
          Under review
        </span>
      </div>
      <ol className="grid gap-6 border-t border-border pt-6">
        <li className="relative grid gap-0.5 pl-8">
          <span aria-hidden="true" className="absolute top-3 -bottom-9 left-1.75 w-0.5 bg-border" />
          <span
            aria-hidden="true"
            className="absolute top-1.75 left-0.5 size-3 rounded-full border-2 border-input bg-card"
          />
          <p className="font-label">Submitted</p>
          <p className="text-caption text-muted-foreground">
            <span className="mr-2 font-strong text-foreground">You</span>26 Oct 2026, 09:14 SAST
          </p>
        </li>
        <li className="relative grid gap-0.5 pl-8">
          <span aria-hidden="true" className="absolute top-3 -bottom-9 left-1.75 w-0.5 bg-border" />
          <span
            aria-hidden="true"
            className="absolute top-1 left-0 size-4 rounded-full border-2 border-primary bg-primary ring-4 ring-primary-subtle"
          />
          <p className="font-title text-heading">Under review</p>
          <p className="text-caption text-muted-foreground">
            <span className="mr-2 font-strong text-foreground">The bank</span>26 Oct 2026, 11:02
            SAST
          </p>
        </li>
        <li className="relative grid gap-0.5 pl-8">
          <span
            aria-hidden="true"
            className="absolute top-1.75 left-0.5 size-3 rounded-full border-2 border-border bg-card"
          />
          <p className="font-label text-muted-foreground">Resolved</p>
          <p className="text-caption text-muted-foreground">
            The outcome is added here when the bank decides.
          </p>
        </li>
      </ol>
    </div>
  );
}

export function StartPage() {
  const headingRef = usePageHeading('Dispute a transaction');

  return (
    <>
      <section
        aria-labelledby="start-heading"
        className="mx-auto grid w-full max-w-(--page-max) gap-8 px-4 pt-8 pb-12 md:px-6 md:py-16 lg:grid-cols-[minmax(0,1fr)_400px] lg:items-center lg:gap-16"
      >
        <div className="grid gap-4">
          <h1
            id="start-heading"
            ref={headingRef}
            tabIndex={-1}
            className="text-display font-title tracking-display text-balance text-heading md:text-display-lg"
          >
            Something wrong with a card transaction?
          </h1>
          <p className="max-w-[68ch] text-muted-foreground">
            Dispute it online in a few steps, then follow its status on one timeline until the bank
            resolves it.
          </p>
          <div className="mt-2 flex flex-col gap-3 md:flex-row">
            <Button asChild>
              <a href={signInUrl}>Sign in</a>
            </Button>
            <Button asChild variant="secondary">
              <a href={signInUrl}>Create a login</a>
            </Button>
          </div>
        </div>
        <figure className="m-0 rounded-lg bg-primary-subtle p-4 py-6 md:p-8">
          <SampleDispute />
          <figcaption className="mt-4 text-caption text-foreground">
            A sample dispute. Every dispute you raise gets a timeline like this one.
          </figcaption>
        </figure>
      </section>
      <section aria-labelledby="how-heading" className="border-t border-border bg-card">
        <div className="mx-auto w-full max-w-(--page-max) px-4 py-12 md:px-6 md:py-16">
          <h2 id="how-heading" className="text-heading font-strong">
            How it works
          </h2>
          <ol className="mt-6 grid gap-6 md:grid-cols-3 md:gap-8">
            {steps.map((step, index) => (
              <li
                key={step.title}
                className="grid grid-cols-[auto_1fr] items-start gap-x-4 gap-y-1"
              >
                <span
                  aria-hidden="true"
                  className="row-span-2 grid size-10 place-items-center rounded-full bg-muted text-subheading font-title text-heading tabular-nums"
                >
                  {index + 1}
                </span>
                <h3 className="text-subheading font-strong">{step.title}</h3>
                <p className="text-muted-foreground">{step.text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>
    </>
  );
}
