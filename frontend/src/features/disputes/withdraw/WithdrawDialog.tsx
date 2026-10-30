import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';

import {
  getGetApiV1DisputesDisputeIdQueryKey,
  getGetApiV1DisputesInfiniteQueryKey,
  getGetApiV1TransactionsInfiniteQueryKey,
  usePostApiV1DisputesDisputeIdWithdraw,
} from '@/api/generated/disputedesk';
import { CustomerStatus } from '@/api/generated/model';
import { ApiError } from '@/api/mutator';
import { isProblem, problemExtension, traceIdOf } from '@/api/problems';
import { useAnnounce } from '@/app/LiveRegion';
import { ProblemNotice } from '@/components/ProblemNotice';
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Textarea } from '@/components/ui/textarea';

import { describeCustomerStatus } from '../customerStatus';

const noteLimit = 500;
const warnAt = 450;
const noteTooLong = 'Your note must be 500 characters or fewer.';
const genericMessage = 'Something went wrong on our side. Try again.';

type Notice = { message: string; traceId?: string };

// The note count is announced once at 90% of the limit and once at the limit (EXPERIENCE.md, Note field).
function noteLevelFor(length: number): number {
  if (length >= noteLimit) return noteLimit;
  return length >= warnAt ? warnAt : 0;
}

function asCustomerStatus(value: string | undefined): CustomerStatus | undefined {
  return Object.values(CustomerStatus).find((known) => known === value);
}

/** True when a 400's `errors` names the note. */
function namesNote(error: ApiError): boolean {
  const named = (error.problem as { errors?: Record<string, unknown> } | undefined)?.errors;
  return named !== undefined && 'note' in named;
}

function WithdrawForm({
  disputeId,
  busy,
  setBusy,
  close,
  onNotFound,
}: {
  disputeId: string;
  busy: boolean;
  setBusy: (busy: boolean) => void;
  close: () => void;
  onNotFound: () => void;
}) {
  const queryClient = useQueryClient();
  const announce = useAnnounce();
  const withdraw = usePostApiV1DisputesDisputeIdWithdraw({ mutation: { retry: false } });
  // Set synchronously in confirm(), because `busy` only changes on the next render.
  const attemptInFlight = useRef(false);
  const summaryRef = useRef<HTMLDivElement>(null);
  const [note, setNote] = useState('');
  const [attempted, setAttempted] = useState(false);
  const [serverRefusedNote, setServerRefusedNote] = useState(false);
  const [summaryRequests, setSummaryRequests] = useState(0);
  const [notice, setNotice] = useState<Notice | undefined>(undefined);

  const trimmed = note.trim();
  const noteLength = trimmed.length;
  const noteLevel = noteLevelFor(noteLength);
  const showNoteError = (attempted && noteLength > noteLimit) || serverRefusedNote;

  const announcedLevel = useRef(0);
  useEffect(() => {
    if (noteLevel > announcedLevel.current) {
      announce(
        noteLevel === noteLimit
          ? `You've reached the limit of ${String(noteLimit)} characters`
          : `${String(noteLimit - noteLength)} characters left`,
      );
    }
    announcedLevel.current = noteLevel;
  }, [noteLevel, noteLength, announce]);

  // The error summary takes focus each time it appears (GOV.UK Error summary).
  useEffect(() => {
    if (summaryRequests > 0) summaryRef.current?.focus();
  }, [summaryRequests]);

  const invalidateDetail = () =>
    queryClient.invalidateQueries({ queryKey: getGetApiV1DisputesDisputeIdQueryKey(disputeId) });

  function finishWithdrawn() {
    void queryClient.invalidateQueries({ queryKey: getGetApiV1DisputesInfiniteQueryKey() });
    void queryClient.invalidateQueries({ queryKey: getGetApiV1TransactionsInfiniteQueryKey() });
    announce('Dispute withdrawn');
    close();
  }

  async function handleFailure(error: Error) {
    if (!(error instanceof ApiError)) {
      setNotice({ message: genericMessage });
      return;
    }
    const traceId = traceIdOf(error);
    if (error.status === 404) {
      onNotFound();
      return;
    }
    if (error.status === 400 && namesNote(error)) {
      setServerRefusedNote(true);
      setSummaryRequests((requests) => requests + 1);
      return;
    }
    if (!isProblem(error, 'action-not-allowed')) {
      setNotice({ message: genericMessage, traceId });
      return;
    }

    const status = asCustomerStatus(problemExtension(error, 'customerStatus'));
    if (status === 'withdrawn') {
      // The dispute is already withdrawn, which is what the customer asked for. Show it before closing, so a retried
      // withdraw shows its result and never implies its note was saved (FR-18).
      await invalidateDetail();
      finishWithdrawn();
    } else if (status === 'resolved') {
      void invalidateDetail();
      setNotice({ message: "This dispute is now Resolved. It can't be withdrawn." });
    } else if (status && describeCustomerStatus(status)?.group === 'open') {
      // A second version clash (FR-24): the dispute is still open, so a retry can succeed.
      void invalidateDetail();
      setNotice({ message: genericMessage, traceId });
    } else {
      setNotice({ message: "This dispute can't be withdrawn any more." });
    }
  }

  async function confirm() {
    if (attemptInFlight.current) return;
    setAttempted(true);
    setServerRefusedNote(false);
    if (noteLength > noteLimit) {
      setSummaryRequests((requests) => requests + 1);
      return;
    }

    attemptInFlight.current = true;
    setBusy(true);
    setNotice(undefined);
    try {
      const dispute = await withdraw.mutateAsync({
        disputeId,
        data: trimmed === '' ? {} : { note: trimmed },
      });
      queryClient.setQueryData(getGetApiV1DisputesDisputeIdQueryKey(disputeId), dispute);
      finishWithdrawn();
    } catch (error) {
      await handleFailure(error instanceof Error ? error : new TypeError('Request failed'));
    } finally {
      attemptInFlight.current = false;
      setBusy(false);
    }
  }

  return (
    <>
      <AlertDialogHeader>
        <AlertDialogTitle>Withdraw this dispute?</AlertDialogTitle>
        <AlertDialogDescription>
          You can&apos;t dispute this transaction again after you withdraw.
        </AlertDialogDescription>
      </AlertDialogHeader>

      {showNoteError ? (
        <div
          ref={summaryRef}
          role="group"
          aria-labelledby="withdraw-error-summary-title"
          tabIndex={-1}
          className="rounded-md border-[1.5px] border-destructive bg-status-danger-bg p-4 text-status-danger-fg"
        >
          {/* GOV.UK Error summary: the summary takes focus and an inner role="alert" announces it. */}
          <div role="alert" className="grid gap-2">
            <h2 id="withdraw-error-summary-title" className="text-body font-strong">
              Check the details below
            </h2>
            <ul className="grid gap-1">
              <li>
                <a
                  href="#withdraw-note"
                  onClick={(event) => {
                    event.preventDefault();
                    document.getElementById('withdraw-note')?.focus();
                  }}
                >
                  {noteTooLong}
                </a>
              </li>
            </ul>
          </div>
        </div>
      ) : null}

      {notice ? <ProblemNotice message={notice.message} traceId={notice.traceId} /> : null}

      <Field data-invalid={showNoteError}>
        <FieldLabel htmlFor="withdraw-note">Note (optional)</FieldLabel>
        <Textarea
          id="withdraw-note"
          value={note}
          onChange={(event) => {
            setNote(event.target.value);
          }}
          aria-invalid={showNoteError}
          aria-describedby={`${showNoteError ? 'withdraw-note-error ' : ''}withdraw-note-count`}
        />
        <FieldDescription id="withdraw-note-count">
          {`${String(noteLength)} / ${String(noteLimit)}`}
        </FieldDescription>
        {showNoteError ? <FieldError id="withdraw-note-error">{noteTooLong}</FieldError> : null}
      </Field>

      <AlertDialogFooter>
        <Button
          type="button"
          variant="destructive"
          aria-disabled={busy}
          onClick={() => void confirm()}
        >
          {busy ? 'Withdrawing…' : 'Withdraw dispute'}
        </Button>
        <AlertDialogCancel
          aria-disabled={busy}
          onClick={(event) => {
            if (busy) event.preventDefault();
          }}
        >
          Keep dispute
        </AlertDialogCancel>
      </AlertDialogFooter>
    </>
  );
}

/**
 * The withdraw confirmation (WAI-ARIA APG, alert dialog). The detail screen owns `open`, so the dialog stays mounted
 * when a refetch removes the Withdraw button. While a request is pending it cannot be closed.
 */
export function WithdrawDialog({
  disputeId,
  open,
  onOpenChange,
  onCloseAutoFocus,
  onNotFound,
}: {
  disputeId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCloseAutoFocus: () => void;
  onNotFound: () => void;
}) {
  const [busy, setBusy] = useState(false);

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent
        onEscapeKeyDown={(event) => {
          if (busy) event.preventDefault();
        }}
        // The trigger is a plain button that may be gone by now, so the screen decides where focus goes.
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          onCloseAutoFocus();
        }}
      >
        <WithdrawForm
          disputeId={disputeId}
          busy={busy}
          setBusy={setBusy}
          close={() => {
            onOpenChange(false);
          }}
          onNotFound={onNotFound}
        />
      </AlertDialogContent>
    </AlertDialog>
  );
}
