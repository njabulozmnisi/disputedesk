import { Link, useLocation, useParams } from '@tanstack/react-router';
import { useRef, useState, type ReactNode, type RefObject } from 'react';

import { useGetApiV1DisputesDisputeId } from '@/api/generated/disputedesk';
import type { DisputeResponse } from '@/api/generated/model';
import { ApiError } from '@/api/mutator';
import { traceIdOf } from '@/api/problems';
import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { formatFullDay, formatMoney } from '@/features/transactions/format';
import { MaskedNumber } from '@/features/transactions/TransactionRow';
import { cn } from '@/lib/utils';

import { describeCustomerStatus } from '../customerStatus';
import { DisputeStatusBadge } from '../DisputeStatusBadge';
import { reasonCopy } from '../raise/reasons';
import { WithdrawDialog } from '../withdraw/WithdrawDialog';
import { Timeline } from './Timeline';

const headingClass = 'text-title font-title tracking-title wrap-anywhere text-heading';
const cardClass = 'grid gap-4 rounded-lg border border-border bg-card p-6 shadow-card';
// The same words as the not-found route (NotFoundPage), for a dispute the API does not know.
const notFoundTitle = "We can't find that dispute";

const rejectedGuidance =
  "If you disagree with this outcome, you can complain to the bank first. If you're still not satisfied after the bank has answered your complaint, you can take it to the National Financial Ombud Scheme South Africa, an independent service that is free for customers.";

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-1 border-t border-border py-3 first:border-t-0 md:grid-cols-[10rem_1fr] md:gap-4">
      <dt className="text-label text-muted-foreground">{label}</dt>
      <dd className="min-w-0 wrap-break-word">{children}</dd>
    </div>
  );
}

function LoadingCard() {
  return (
    <>
      <Skeleton className="w-24" />
      <Skeleton className="w-2/5" />
    </>
  );
}

function LoadingTimeline() {
  return (
    <ol className="grid gap-4">
      {[1, 2, 3].map((placeholder) => (
        <li key={placeholder} className="grid gap-2">
          <Skeleton className="w-3/5" />
          <Skeleton className="w-2/5" />
        </li>
      ))}
    </ol>
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
      <div>
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
        <p className="text-amount-hero font-title tabular-nums">
          {formatMoney(dispute.disputedAmount)}
        </p>
      </div>
      <MaskedNumber row={transaction} />
      <dl>
        {reasonLabel ? <Row label="Reason">{reasonLabel}</Row> : null}
        <Row label="Transaction date">{formatFullDay(transaction.transactionDate)}</Row>
        {dispute.expectedAmount ? (
          <>
            <Row label="Transaction amount">{formatMoney(transaction.amount)}</Row>
            <Row label="Expected amount">{formatMoney(dispute.expectedAmount)}</Row>
          </>
        ) : null}
      </dl>
      {status?.group === 'open' ? (
        <div>
          <Button
            ref={withdrawButtonRef}
            type="button"
            variant="secondary"
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
  const { disputeId } = useParams({ from: '/disputes/$disputeId' });
  const query = useGetApiV1DisputesDisputeId(disputeId);
  // Set when a withdraw is answered 404: the dispute is gone, whatever the cache still holds.
  const [goneDisputeId, setGoneDisputeId] = useState<string>();
  const justSubmitted = useLocation({
    select: (location) => location.state.justSubmitted === true,
  });

  const isNotFound =
    goneDisputeId === disputeId ||
    (query.isError && query.error instanceof ApiError && query.error.status === 404);
  // The query's error is checked before its data, so a failed refetch never leaves a stale detail on screen: the loaded
  // card and the withdraw dialog it owns give way to the failure and Try again (criterion 2).
  const dispute = isNotFound || query.isError ? undefined : query.data;
  const failure = isNotFound || !query.isError ? undefined : query.error;
  const isMessage = isNotFound || failure !== undefined;
  const isLoading = !isMessage && !dispute;

  let title = 'Dispute';
  if (isNotFound) title = notFoundTitle;
  else if (dispute) title = dispute.transaction.description;
  const headingRef = usePageHeading(title);

  const failureCard = failure ? (
    <FailureCard
      error={failure}
      onRetry={() => {
        // The Try again button unmounts on success, so focus goes to the heading (WCAG 2.2 SC 2.4.3).
        void query.refetch().then((result) => {
          if (result.isSuccess) headingRef.current?.focus();
        });
      }}
    />
  ) : null;

  let card = <LoadingCard />;
  let timeline = <LoadingTimeline />;
  if (isNotFound) {
    card = <NotFoundCard />;
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
        {dispute.outcome === 'rejected' ? (
          <p className="text-muted-foreground">{rejectedGuidance}</p>
        ) : null}
      </>
    );
  } else if (failureCard) {
    card = failureCard;
  }

  return (
    <Page width={isMessage ? 'form' : 'content'}>
      {isMessage ? null : (
        <div>
          <Link to="/disputes">Back to disputes</Link>
        </div>
      )}

      {dispute && justSubmitted ? (
        <div className="rounded-md bg-status-success-bg p-4 text-status-success-fg">
          <strong className="font-strong">Dispute submitted</strong>
        </div>
      ) : null}

      <div
        aria-busy={isLoading ? true : undefined}
        className={cn('grid gap-6', !isMessage && 'lg:grid-cols-2 lg:items-start lg:gap-8')}
      >
        <section className={isMessage ? 'grid gap-4' : cardClass}>
          <h1 ref={headingRef} tabIndex={-1} className={headingClass}>
            {title}
          </h1>
          {card}
        </section>
        {isMessage ? null : <div className="grid gap-6">{timeline}</div>}
      </div>
    </Page>
  );
}
