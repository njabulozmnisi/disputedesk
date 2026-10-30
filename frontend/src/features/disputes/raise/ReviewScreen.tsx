import type { Span } from '@opentelemetry/api';
import { useQueryClient, type Query } from '@tanstack/react-query';
import { Link, Navigate, useRouter } from '@tanstack/react-router';
import { useEffect, useRef, type ReactNode } from 'react';

import {
  getGetApiV1TransactionsInfiniteQueryKey,
  postApiV1Disputes,
  usePostApiV1Disputes,
} from '@/api/generated/disputedesk';
import type { TransactionResponse } from '@/api/generated/model';
import { ApiError } from '@/api/mutator';
import { isProblem } from '@/api/problems';
import { useAnnounce } from '@/app/LiveRegion';
import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { Button } from '@/components/ui/button';
import { endRaiseSubmit, startRaiseSubmit, withRaiseSubmit } from '@/telemetry/raiseSpans';
import { formatFullDay, formatMoney } from '@/features/transactions/format';
import { MaskedNumber } from '@/features/transactions/TransactionRow';

import { useRaiseDraft, type Draft, type FieldName, type ServerErrors } from './RaiseDraft';
import { useRaiseOpenSpan, useRaiseRow } from './RaiseLayout';
import { reasonCopy } from './reasons';
import { disputedAmount, messages, serverAmountMessage } from './raiseSchema';
import { endsTheDraft, SubmitFailure } from './SubmitFailure';

// An attempt that is still in progress on the server is resent once a second, at most five times (EXPERIENCE.md,
// Problem-type mapping).
const maxResends = 5;
const resendDelayMs = 1000;

function isFieldName(key: string): key is FieldName {
  return key === 'reason' || key === 'note' || key === 'expectedAmount';
}

/**
 * A 400's `errors` keys (camelCase) name request fields; any other key, or no `errors`, is a form-level error. The
 * API's own text is never shown: each field gets the screen's copy.
 */
function serverErrorsFrom(error: ApiError, transactionAmount: string): ServerErrors {
  const fieldCopy: Record<FieldName, string> = {
    reason: messages.reasonMissing,
    note: messages.noteTooLong,
    expectedAmount: serverAmountMessage(transactionAmount),
  };
  const named = (error.problem as { errors?: Record<string, unknown> } | undefined)?.errors ?? {};
  const result: ServerErrors = { fields: {}, formError: Object.keys(named).length === 0 };
  for (const key of Object.keys(named)) {
    if (isFieldName(key)) result.fields[key] = fieldCopy[key];
    else result.formError = true;
  }
  return result;
}

const isDisputesQuery = (query: Query) =>
  query.queryKey.some((part) => typeof part === 'string' && part.startsWith('/api/v1/disputes'));

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-1 border-t border-border py-3 first:border-t-0 md:grid-cols-[8rem_1fr] md:gap-4">
      <dt className="text-label text-muted-foreground">{label}</dt>
      <dd className="min-w-0 wrap-break-word">{children}</dd>
    </div>
  );
}

function buttonLabel(pending: boolean, failed: boolean): string {
  if (pending) return 'Submitting…';
  return failed ? 'Try again' : 'Submit dispute';
}

function ReviewForm({ row, draft }: { row: TransactionResponse; draft: Draft }) {
  const headingRef = usePageHeading('Review your dispute');
  const queryClient = useQueryClient();
  const router = useRouter();
  const announce = useAnnounce();
  const { markSubmitted, setServerErrors } = useRaiseDraft();
  const raisePath = '/transactions/$transactionId/dispute';
  const reviewPath = '/transactions/$transactionId/dispute/review';
  // Set synchronously in send(), because isPending only changes on the next render.
  const attemptInFlight = useRef(false);
  // The `dispute.raise.submit` span of the attempt in flight: started by send(), ended when the mutation settles.
  const submitSpan = useRef<Span | undefined>(undefined);
  const openSpan = useRaiseOpenSpan();

  const invalidateTransactions = () =>
    queryClient.invalidateQueries({ queryKey: getGetApiV1TransactionsInfiniteQueryKey() });

  // Every attempt for this draft goes through the generated `postApiV1Disputes` with the draft's request and key, so
  // each one carries `X-XSRF-TOKEN` from `customFetch`. `mutationFn` runs again for each resend, so it runs the call
  // inside the one submit span of the attempt.
  const mutation = usePostApiV1Disputes({
    mutation: {
      mutationFn: ({ data }) => {
        const span = submitSpan.current;
        if (!span) throw new Error('send() starts the submit span');
        return withRaiseSubmit(span, () =>
          postApiV1Disputes(data, { headers: { 'Idempotency-Key': draft.key } }),
        );
      },
      retry: (failureCount, error) =>
        failureCount < maxResends && isProblem(error, 'idempotency-in-progress'),
      retryDelay: resendDelayMs,
      onSuccess: (dispute) => {
        void invalidateTransactions();
        void queryClient.invalidateQueries({ predicate: isDisputesQuery });
        announce('Dispute submitted');
        const stillOnReview = router.matchRoute({
          to: reviewPath,
          params: { transactionId: row.transactionId },
        });
        // After browser Back the customer is elsewhere: mark the draft submitted but do not navigate.
        if (!stillOnReview) {
          markSubmitted(row.transactionId);
          return;
        }
        // Navigate first: once the draft is marked submitted, the raise routes send Back to /transactions.
        void router
          .navigate({
            to: '/disputes/$disputeId',
            params: { disputeId: dispute.disputeId },
            replace: true,
            state: { justSubmitted: true },
          })
          .then(() => {
            markSubmitted(row.transactionId);
          });
      },
      onError: (error) => {
        if (isProblem(error, 'already-disputed') || isProblem(error, 'not-disputable')) {
          void invalidateTransactions();
        }
        if (error instanceof ApiError && isProblem(error, 'validation')) {
          setServerErrors(serverErrorsFrom(error, row.amount.amount));
          void router.navigate({
            to: raisePath,
            params: { transactionId: row.transactionId },
            replace: true,
          });
        }
      },
      onSettled: (_dispute, error) => {
        if (submitSpan.current) endRaiseSubmit(submitSpan.current, error);
        attemptInFlight.current = false;
      },
    },
  });

  // `aria-disabled`, not `disabled`, so the button keeps focus while an attempt is pending.
  function send() {
    if (attemptInFlight.current) return;
    attemptInFlight.current = true;
    submitSpan.current = startRaiseSubmit(openSpan.current);
    mutation.mutate({ data: draft.request });
  }

  const error = mutation.error;
  const refused = error && endsTheDraft(error);

  // The refusal replaces the review card, taking Submit (and its focus) with it: focus the heading instead.
  useEffect(() => {
    if (refused) headingRef.current?.focus();
  }, [refused, headingRef]);

  const pending = mutation.isPending;
  const submitLabel = buttonLabel(pending, Boolean(error));
  const amountToDispute = disputedAmount(draft.values, row.amount.amount);

  return (
    <Page width="form">
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-title font-title tracking-title wrap-break-word text-heading"
      >
        Review your dispute
      </h1>

      {error ? <SubmitFailure error={error} /> : null}

      {refused ? null : (
        <>
          <div className="grid gap-1 rounded-lg border border-border bg-card p-6">
            <p className="text-label text-muted-foreground">You&apos;re disputing</p>
            <p className="text-amount-hero font-title tabular-nums">
              {formatMoney({ amount: amountToDispute, currency: row.amount.currency })}
            </p>
          </div>

          <dl className="rounded-md border border-border bg-card px-4">
            <Row label="Transaction">{row.description}</Row>
            <Row label="Date">{formatFullDay(row.transactionDate)}</Row>
            <Row label={row.maskedCard ? 'Card' : 'Account'}>
              <MaskedNumber row={row} />
            </Row>
            <Row label="Reason">
              {draft.request.reason ? reasonCopy[draft.request.reason].label : null}
            </Row>
            {draft.request.note ? <Row label="Note">{draft.request.note}</Row> : null}
          </dl>

          <div>
            <Link
              to={raisePath}
              params={{ transactionId: row.transactionId }}
              disabled={pending}
              className="aria-disabled:cursor-progress aria-disabled:opacity-70"
            >
              Change
            </Link>
          </div>

          <p className="text-muted-foreground">
            You can dispute a transaction only once. You can withdraw the dispute while it is open.
          </p>

          <div className="flex flex-wrap items-center gap-4">
            <Button type="button" aria-disabled={pending} onClick={send}>
              {submitLabel}
            </Button>
            <Link
              to="/transactions"
              disabled={pending}
              className="aria-disabled:cursor-progress aria-disabled:opacity-70"
            >
              Cancel
            </Link>
          </div>
        </>
      )}
    </Page>
  );
}

/** Screen 4b: read the draft back, then Submit dispute. With no draft the customer is sent to 4a. */
export function ReviewScreen() {
  const row = useRaiseRow();
  const draft = useRaiseDraft().draftFor(row.transactionId);

  if (!draft) {
    return (
      <Navigate
        to="/transactions/$transactionId/dispute"
        params={{ transactionId: row.transactionId }}
        replace
      />
    );
  }
  return <ReviewForm row={row} draft={draft} />;
}
