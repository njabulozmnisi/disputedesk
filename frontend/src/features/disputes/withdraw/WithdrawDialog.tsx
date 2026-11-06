import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { z } from 'zod';

import {
  getGetDisputeQueryKey,
  getListDisputesInfiniteQueryKey,
  getListTransactionsInfiniteQueryKey,
  getWithdrawDisputeMutationKey,
  useWithdrawDispute,
} from '@/api/generated/disputedesk';
import { CustomerStatus } from '@/api/generated/model';
import { isProblem, problemExtension, traceIdOf, validationFields } from '@/api/problems';
import { useAnnounce } from '@/app/LiveRegion';
import { ErrorSummary } from '@/components/ErrorSummary';
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

// The limit counts the trimmed note, which is also what the request sends.
const withdrawSchema = z.object({
  note: z.string().refine((note) => note.trim().length <= noteLimit, { error: noteTooLong }),
});
type WithdrawFormValues = z.infer<typeof withdrawSchema>;

// The note count is announced once at 90% of the limit and once at the limit (EXPERIENCE.md, Note field).
function noteLevelFor(length: number): number {
  if (length >= noteLimit) return noteLimit;
  return length >= warnAt ? warnAt : 0;
}

function asCustomerStatus(value: string | undefined): CustomerStatus | undefined {
  return Object.values(CustomerStatus).find((known) => known === value);
}

const mutationKey = getWithdrawDisputeMutationKey();

function WithdrawForm({
  disputeId,
  close,
  onNotFound,
}: {
  disputeId: string;
  close: () => void;
  onNotFound: () => void;
}) {
  const queryClient = useQueryClient();
  const announce = useAnnounce();
  const { control, handleSubmit, setError, formState } = useForm<WithdrawFormValues>({
    resolver: zodResolver(withdrawSchema),
    defaultValues: { note: '' },
    // The error summary owns focus (GOV.UK Error summary); the default would move it to the field.
    shouldFocusError: false,
  });
  const [summaryRequests, setSummaryRequests] = useState(0);
  const [notice, setNotice] = useState<Notice | undefined>(undefined);

  const noteLength = useWatch({ control, name: 'note' }).trim().length;
  const noteLevel = noteLevelFor(noteLength);

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

  const invalidateDetail = () =>
    queryClient.invalidateQueries({ queryKey: getGetDisputeQueryKey(disputeId) });

  function finishWithdrawn() {
    void queryClient.invalidateQueries({ queryKey: getListDisputesInfiniteQueryKey() });
    void queryClient.invalidateQueries({ queryKey: getListTransactionsInfiniteQueryKey() });
    announce('Dispute withdrawn');
    close();
  }

  const withdraw = useWithdrawDispute({
    mutation: {
      retry: false,
      onSuccess: (dispute) => {
        queryClient.setQueryData(getGetDisputeQueryKey(disputeId), dispute);
        finishWithdrawn();
      },
      onError: async (error) => {
        if (error instanceof TypeError) {
          setNotice({ message: genericMessage });
          return;
        }
        if (isProblem(error, 'not-found')) {
          onNotFound();
          return;
        }
        if (isProblem(error, 'validation') && validationFields(error).includes('note')) {
          setError('note', { message: noteTooLong });
          setSummaryRequests((requests) => requests + 1);
          return;
        }
        if (!isProblem(error, 'action-not-allowed')) {
          setNotice({ message: genericMessage, traceId: traceIdOf(error) });
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
          setNotice({ message: genericMessage, traceId: traceIdOf(error) });
        } else {
          setNotice({ message: "This dispute can't be withdrawn any more." });
        }
      },
    },
  });

  function send({ note }: WithdrawFormValues) {
    // Read from the cache, not from `isPending`: a second press in the same tick finds the first request pending here
    // before the screen re-renders (TanStack Query, QueryClient.isMutating).
    if (queryClient.isMutating({ mutationKey }) > 0) return;
    const trimmed = note.trim();
    setNotice(undefined);
    withdraw.mutate({ disputeId, data: trimmed === '' ? {} : { note: trimmed } });
  }

  const noteError = formState.errors.note;

  return (
    <>
      <AlertDialogHeader>
        <AlertDialogTitle>Withdraw this dispute?</AlertDialogTitle>
        <AlertDialogDescription>
          You can&apos;t dispute this transaction again after you withdraw.
        </AlertDialogDescription>
      </AlertDialogHeader>

      <form
        noValidate
        className="grid gap-4"
        onSubmit={(event) => {
          void handleSubmit(send, () => {
            setSummaryRequests((requests) => requests + 1);
          })(event);
        }}
      >
        {summaryRequests > 0 && noteError?.message ? (
          <ErrorSummary
            items={[{ fieldId: 'withdraw-note', message: noteError.message }]}
            requests={summaryRequests}
          />
        ) : null}

        {notice ? <ProblemNotice message={notice.message} traceId={notice.traceId} /> : null}

        <Controller
          name="note"
          control={control}
          render={({ field, fieldState }) => (
            <Field data-invalid={fieldState.invalid}>
              <FieldLabel htmlFor="withdraw-note">Note (optional)</FieldLabel>
              <Textarea
                {...field}
                id="withdraw-note"
                aria-invalid={fieldState.invalid}
                aria-describedby={`${fieldState.error ? 'withdraw-note-error ' : ''}withdraw-note-count`}
              />
              <FieldDescription id="withdraw-note-count">
                {`${String(noteLength)} / ${String(noteLimit)}`}
              </FieldDescription>
              {fieldState.error ? (
                <FieldError id="withdraw-note-error">{fieldState.error.message}</FieldError>
              ) : null}
            </Field>
          )}
        />

        <AlertDialogFooter>
          <Button type="submit" variant="destructive" aria-disabled={withdraw.isPending}>
            {withdraw.isPending ? 'Withdrawing…' : 'Withdraw dispute'}
          </Button>
          <AlertDialogCancel
            aria-disabled={withdraw.isPending}
            onClick={(event) => {
              if (withdraw.isPending) event.preventDefault();
            }}
          >
            Keep dispute
          </AlertDialogCancel>
        </AlertDialogFooter>
      </form>
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
  const queryClient = useQueryClient();

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent
        onEscapeKeyDown={(event) => {
          // The form owns the request; the dialog reads the cache when Escape is pressed, as `send` does, so it never
          // acts on a render that has not caught up with the request (TanStack Query, QueryClient.isMutating).
          if (queryClient.isMutating({ mutationKey }) > 0) event.preventDefault();
        }}
        // The trigger is a plain button that may be gone by now, so the screen decides where focus goes.
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          onCloseAutoFocus();
        }}
      >
        <WithdrawForm
          disputeId={disputeId}
          close={() => {
            onOpenChange(false);
          }}
          onNotFound={onNotFound}
        />
      </AlertDialogContent>
    </AlertDialog>
  );
}
