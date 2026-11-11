import { Link, useLocation, useParams } from '@tanstack/react-router';
import { Search } from 'lucide-react';
import { useRef, useState, type ReactNode, type RefObject } from 'react';

import { useGetDispute } from '@/api/generated/disputedesk';
import type { DisputeResponse } from '@/api/generated/model';
import type { ApiError } from '@/api/mutator';
import { isProblem, traceIdOf } from '@/api/problems';
import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { ProblemNotice } from '@/components/ProblemNotice';
import { StateSurface } from '@/components/StateSurface';
import { Alert, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { formatDayWithYear, formatFullDay, formatMoney } from '@/features/transactions/format';
import { MaskedNumber } from '@/features/transactions/TransactionRow';
import { cn } from '@/lib/utils';

import { describeCustomerStatus } from '../customerStatus';
import { DisputeStatusBadge } from '../DisputeStatusBadge';
import { reasonCopy } from '../raise/reasons';
import { WithdrawDialog } from '../withdraw/WithdrawDialog';
import { surfaceClass } from './surfaceClass';
import { Timeline } from './Timeline';

const headingClass = 'text-title font-title tracking-title wrap-anywhere text-heading';
const cardClass = 'grid gap-4 rounded-lg border border-border bg-card p-6 shadow-card';
// The same words as the not-found route (NotFoundPage), for a dispute the API does not know.
const notFoundTitle = "We can't find that dispute";

const rejectedSteps = [
  'You can complain to the bank first.',
  "If you're still not satisfied after the bank has answered your complaint, you can take it to the National Financial Ombud Scheme South Africa, an independent service that is free for customers.",
];

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-1 border-t border-border py-3 first:border-t-0 md:grid-cols-[10rem_1fr] md:gap-4">
      <dt className="text-label text-muted-foreground">{label}</dt>
      <dd className="min-w-0 wrap-break-word">{children}</dd>
    </div>
  );
}

// The skeleton matches the loaded layout: the summary card's bars under the real h1, then a timeline surface with three
// entries, a dot and two bars each. Bar widths are classes, not inline styles.
function LoadingCard() {
  return (
    <>
      <Skeleton className="h-5.5 w-2/5" />
      <div className="grid gap-2">
        <Skeleton className="h-2.5 w-1/4" />
        <Skeleton className="h-8 w-2/5" />
        <Skeleton className="h-2.5 w-1/2" />
      </div>
      <Skeleton className="w-full" />
      <Skeleton className="w-full" />
    </>
  );
}

function LoadingTimeline() {
  return (
    <div className={surfaceClass}>
      <Skeleton className="h-5.5 w-1/3" />
      <ol className="grid gap-4">
        {[1, 2, 3].map((placeholder) => (
          <li key={placeholder} className="flex items-start gap-4">
            <span aria-hidden="true" className="mt-1 size-3 shrink-0 rounded-full bg-muted" />
            <div className="grid flex-1 gap-2">
              <Skeleton className="w-3/4" />
              <Skeleton className="h-2.5 w-1/2" />
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}

/** DESIGN.md, Rejected guidance: its own flat surface below the timeline, with numbered steps like the start page's. */
function RejectedGuidance() {
  return (
    <section className={surfaceClass} aria-labelledby="guidance-heading">
      <h2 id="guidance-heading" className="text-subheading font-strong">
        If you disagree with this outcome
      </h2>
      <ol className="grid gap-3">
        {rejectedSteps.map((step, index) => (
          <li key={step} className="flex gap-3">
            <span
              aria-hidden="true"
              className="grid size-7 shrink-0 place-items-center rounded-full bg-muted text-label font-title text-heading"
            >
              {index + 1}
            </span>
            <p>{step}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}

function FailureCard({ error, onRetry }: { error: ApiError | TypeError; onRetry: () => void }) {
  return (
    <>
      <ProblemNotice
        message="Something went wrong on our side. Try again."
        traceId={traceIdOf(error)}
      />
      <div>
        <Button type="button" onClick={onRetry}>
          Try again
        </Button>
      </div>
    </>
  );
}

function NotFoundCard() {
  return (
    <>
      <p className="text-muted-foreground">Check the link, or find it in your disputes.</p>
      <div className="mt-3">
        <Button asChild>
          <Link to="/disputes">Go to your disputes</Link>
        </Button>
      </div>
    </>
  );
}

/**
 * The loaded dispute's summary. It owns the withdraw dialog's `open` state, so a refetch that removes the Withdraw
 * button (the dispute was resolved meanwhile) does not unmount the dialog and its notice.
 */
function LoadedCard({
  dispute,
  disputeId,
  headingRef,
  onGone,
}: {
  dispute: DisputeResponse;
  disputeId: string;
  headingRef: RefObject<HTMLHeadingElement | null>;
  onGone: () => void;
}) {
  const withdrawButtonRef = useRef<HTMLButtonElement>(null);
  const [withdrawOpen, setWithdrawOpen] = useState(false);
  const status = describeCustomerStatus(dispute.customerStatus, dispute.outcome);
  // A reason this build does not know yet has no label; the raw value is never shown.
  const reasonLabel = Object.hasOwn(reasonCopy, dispute.reason)
    ? reasonCopy[dispute.reason].label
    : undefined;
  const { transaction } = dispute;

  // After the dialog closes: the Withdraw button if it is still shown, else the heading (WCAG 2.2 SC 2.4.3).
  function returnFocus() {
    (withdrawButtonRef.current ?? headingRef.current)?.focus();
  }

  return (
    <>
      {status ? (
        <div>
          <DisputeStatusBadge statusDescription={status} />
        </div>
      ) : null}
      <div>
        <p className="text-caption text-muted-foreground">Disputed amount</p>
        <p className="text-amount-hero font-title wrap-anywhere tabular-nums">
          {formatMoney(dispute.disputedAmount)}
        </p>
      </div>
      <MaskedNumber row={transaction} />
      <dl className="border-t border-border">
        {reasonLabel ? <Row label="Reason">{reasonLabel}</Row> : null}
        {dispute.expectedAmount ? (
          <>
            <Row label="Transaction amount">{formatMoney(transaction.amount)}</Row>
            <Row label="Expected amount">{formatMoney(dispute.expectedAmount)}</Row>
          </>
        ) : null}
        <Row label="Transaction date">{formatFullDay(transaction.transactionDate)}</Row>
        {/* From createdAt, which the contract guarantees; the timeline's order is only a mapping convention. */}
        <Row label="Disputed on">
          <time dateTime={dispute.createdAt}>{formatDayWithYear(dispute.createdAt)}</time>
        </Row>
      </dl>
      {status?.group === 'open' ? (
        <div className="grid justify-items-start gap-3 border-t border-border pt-4">
          <p className="text-caption text-muted-foreground">
            Changed your mind? You can withdraw while the dispute is open. You can&apos;t dispute
            this transaction again after you withdraw.
          </p>
          <Button
            ref={withdrawButtonRef}
            type="button"
            variant="secondary"
            className="w-full md:w-auto"
            onClick={() => {
              setWithdrawOpen(true);
            }}
          >
            Withdraw dispute
          </Button>
        </div>
      ) : null}
      <WithdrawDialog
        disputeId={disputeId}
        open={withdrawOpen}
        onOpenChange={setWithdrawOpen}
        onCloseAutoFocus={returnFocus}
        onNotFound={onGone}
      />
    </>
  );
}

/**
 * Screen 6: the dispute's summary, timeline and, while it is open, Withdraw (EXPERIENCE.md, 6).
 *
 * Loading, failure, not-found and the loaded dispute share one `h1` element at one place in the tree. It reads
 * "Dispute" until the data arrives, so a direct load of the URL is one screen that leaves focus alone, and a route
 * change to it focuses the heading (EXPERIENCE.md, Accessibility Floor).
 */
export function DisputeDetailScreen() {
  const { disputeId } = useParams({ from: '/_linked/disputes/$disputeId' });
  const query = useGetDispute(disputeId);
  // Set when a withdraw is answered 404: the dispute is gone, whatever the cache still holds.
  const [goneDisputeId, setGoneDisputeId] = useState<string>();
  const justSubmitted = useLocation({
    select: (location) => location.state.justSubmitted === true,
  });

  const isNotFound =
    goneDisputeId === disputeId || (query.isError && isProblem(query.error, 'not-found'));
  // The query's error is checked before its data, so a failed refetch never leaves a stale detail on screen: the loaded
  // card and the withdraw dialog it owns give way to the failure and Try again (criterion 2).
  const dispute = isNotFound || query.isError ? undefined : query.data;
  const isMessage = isNotFound || query.isError;
  const isLoading = !isMessage && !dispute;

  let title = 'Dispute';
  if (isNotFound) title = notFoundTitle;
  else if (dispute) title = dispute.transaction.description;
  const headingRef = usePageHeading(title);

  let card = <LoadingCard />;
  let timeline = <LoadingTimeline />;
  // Not found is a state surface, which draws its own classes; the failure message and the card are styled here.
  let sectionClass: string | undefined = cardClass;
  if (isNotFound) {
    card = <NotFoundCard />;
    sectionClass = undefined;
  } else if (dispute) {
    card = (
      <LoadedCard
        dispute={dispute}
        disputeId={disputeId}
        headingRef={headingRef}
        onGone={() => {
          setGoneDisputeId(disputeId);
        }}
      />
    );
    timeline = (
      <>
        <Timeline dispute={dispute} />
        {dispute.outcome === 'rejected' ? <RejectedGuidance /> : null}
      </>
    );
  } else if (query.isError) {
    sectionClass = 'grid gap-4';
    card = (
      <FailureCard
        error={query.error}
        onRetry={() => {
          // The Try again button unmounts unless the retry fails again, so focus goes to the heading (WCAG 2.2 SC 2.4.3).
          void query.refetch().then((result) => {
            const gone = isProblem(result.error, 'not-found');
            if (result.isSuccess || gone) headingRef.current?.focus();
          });
        }}
      />
    );
  }

  return (
    <Page width={isMessage ? 'form' : 'content'}>
      {isMessage ? null : (
        <div>
          <Link to="/disputes" className="inline-flex min-h-(--tap-target) items-center">
            Back to disputes
          </Link>
        </div>
      )}

      {dispute && justSubmitted ? (
        <Alert tone="success">
          <AlertTitle>Dispute submitted</AlertTitle>
        </Alert>
      ) : null}

      <div
        aria-busy={isLoading ? true : undefined}
        className={cn(
          'grid grid-cols-1 gap-6',
          !isMessage && 'lg:grid-cols-2 lg:items-start lg:gap-8',
        )}
      >
        {/* One StateSurface in every state, so the h1 is one element at one place in the tree and keeps focus. Only not
            found is drawn as a state surface (it gets an icon); the other states style the section as before. */}
        <StateSurface
          icon={isNotFound ? Search : undefined}
          className={sectionClass}
          heading={
            <h1 ref={headingRef} tabIndex={-1} className={headingClass}>
              {title}
            </h1>
          }
        >
          {card}
        </StateSurface>
        {isMessage ? null : <div className="grid gap-6">{timeline}</div>}
      </div>
    </Page>
  );
}
