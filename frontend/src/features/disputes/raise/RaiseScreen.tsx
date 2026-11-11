import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';

import { Reason } from '@/api/generated/model';
import { useAnnounce } from '@/app/LiveRegion';
import { usePageHeading } from '@/app/usePageHeading';
import { ErrorSummary } from '@/components/ErrorSummary';
import { Page } from '@/components/Page';
import { StepProgress } from '@/components/StepProgress';
import { Button } from '@/components/ui/button';
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { Textarea } from '@/components/ui/textarea';
import { formatMoney } from '@/features/transactions/format';

import { useRaiseDraft, type FieldName } from './RaiseDraft';
import { useRaiseRow } from './RaiseLayout';
import { reasonCopy, reasons } from './reasons';
import {
  buildRequest,
  createRaiseSchema,
  disputedAmount,
  emptyValues,
  expectedAmountError,
  noteLimit,
  type RaiseFormValues,
} from './raiseSchema';
import { TransactionSummary } from './TransactionSummary';

// The note count is announced once at 90% of the limit and once at the limit (EXPERIENCE.md, Note field).
const warnAt = 900;

function noteLevelFor(length: number): number {
  if (length >= noteLimit) return noteLimit;
  return length >= warnAt ? warnAt : 0;
}

const formError = "We couldn't check your dispute. Review the details and try again.";

// The order the fields appear in, with the control the summary link focuses.
const fields: { name: FieldName; focusId: string }[] = [
  { name: 'reason', focusId: `reason-${reasons[0] ?? ''}` },
  { name: 'expectedAmount', focusId: 'expectedAmount' },
  { name: 'note', focusId: 'note' },
];

/** Screen 4a: reason, optional note and, for Wrong amount, the expected amount (EXPERIENCE.md, 4a). */
export function RaiseScreen() {
  const headingRef = usePageHeading('Dispute this transaction');
  const announce = useAnnounce();
  const navigate = useNavigate();
  const row = useRaiseRow();
  const { draftFor, continueWith, serverErrors, setServerErrors } = useRaiseDraft();

  // A 400 on 4b left its errors in the draft state; they are shown once, here.
  const [returnedErrors] = useState(serverErrors);
  useEffect(() => {
    setServerErrors(undefined);
  }, [setServerErrors]);

  const schema = createRaiseSchema(row.amount.amount);
  const { control, handleSubmit, setValue, setError, clearErrors, formState } =
    useForm<RaiseFormValues>({
      resolver: zodResolver(schema),
      defaultValues: draftFor(row.transactionId)?.values ?? emptyValues,
      // Checks when a field is first left, then on every change, so a corrected value drops its error before the
      // person presses Continue (an error that goes on blur would move Continue between pointer down and up).
      mode: 'onTouched',
      // The error summary owns focus (GOV.UK Error summary); the default would move it to a field after the summary.
      shouldFocusError: false,
    });
  useEffect(() => {
    for (const { name } of fields) {
      const message = returnedErrors?.fields[name];
      if (message) setError(name, { message });
    }
  }, [returnedErrors, setError]);

  // The error summary shows after a failed Continue or a returned 400, and takes focus each time.
  const [summaryRequests, setSummaryRequests] = useState(returnedErrors ? 1 : 0);
  const summaryRef = useRef<HTMLDivElement>(null);
  // React runs a child's effects before its parent's, and a parent's in the order they are declared. A summary that
  // mounts with the screen focuses itself first and then loses focus to the heading (usePageHeading), so the screen
  // gives it focus again here, after that hook's effect.
  useEffect(() => {
    summaryRef.current?.focus();
  }, [summaryRequests]);

  const watched = useWatch({ control });
  const values: RaiseFormValues = { ...emptyValues, ...watched };

  const noteLength = values.note.trim().length;
  const noteLevel = noteLevelFor(noteLength);
  const announcedLevel = useRef(0);
  useEffect(() => {
    if (noteLevel > announcedLevel.current) {
      announce(
        noteLevel === noteLimit
          ? `You've reached the limit of 1,000 characters`
          : `${String(noteLimit - noteLength)} characters left`,
      );
    }
    announcedLevel.current = noteLevel;
  }, [noteLevel, noteLength, announce]);

  const isWrongAmount = values.reason === Reason.wrongAmount;
  const showDifference =
    isWrongAmount &&
    values.expectedAmount.trim() !== '' &&
    !expectedAmountError(values.expectedAmount, row.amount.amount);

  function onValid(formValues: RaiseFormValues) {
    continueWith(formValues, buildRequest(formValues, row));
    void navigate({
      to: '/transactions/$transactionId/dispute/review',
      params: { transactionId: row.transactionId },
    });
  }

  const summaryItems = fields.flatMap(({ name, focusId }) => {
    const message = formState.errors[name]?.message;
    return message ? [{ fieldId: focusId, message }] : [];
  });
  const showSummary = summaryRequests > 0 && (summaryItems.length > 0 || returnedErrors?.formError);

  /** `aria-describedby` for a field: its error element when it has an error, then any hint. */
  const describedBy = (name: FieldName, hint?: string) =>
    [formState.errors[name] ? `${name}-error` : undefined, hint].filter(Boolean).join(' ') ||
    undefined;

  return (
    <Page width="form">
      <div className="grid gap-2">
        <StepProgress label="Step 1 of 2: reason and note" step={1} />
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-title font-title tracking-title wrap-break-word text-heading"
        >
          Dispute this transaction
        </h1>
      </div>

      {showSummary ? (
        <ErrorSummary
          ref={summaryRef}
          items={summaryItems}
          requests={summaryRequests}
          formError={returnedErrors?.formError ? formError : undefined}
        />
      ) : null}

      <form
        noValidate
        className="rounded-lg border border-border bg-card px-4 py-6 md:p-8"
        onSubmit={(event) => {
          void handleSubmit(onValid, () => {
            setSummaryRequests((requests) => requests + 1);
          })(event);
        }}
      >
        <FieldGroup>
          <TransactionSummary row={row} />

          <Controller
            name="reason"
            control={control}
            render={({ field, fieldState }) => (
              <div className="grid gap-3">
                <RadioGroup
                  asChild
                  value={field.value}
                  onValueChange={(value) => {
                    field.onChange(value);
                    if (value !== Reason.wrongAmount) {
                      setValue('expectedAmount', '');
                      clearErrors('expectedAmount');
                    }
                  }}
                  aria-invalid={fieldState.invalid}
                  aria-describedby={describedBy('reason')}
                >
                  {/* A fieldset with a legend names the group (WAI, Grouping Controls); Radix adds role="radiogroup". */}
                  <FieldSet>
                    <FieldLegend>Why are you disputing it?</FieldLegend>
                    {reasons.map((reason) => (
                      <div
                        key={reason}
                        className="flex gap-3 rounded-md border border-border bg-card p-4 has-data-[state=checked]:border-primary has-data-[state=checked]:bg-primary-subtle"
                      >
                        <RadioGroupItem
                          id={`reason-${reason}`}
                          value={reason}
                          aria-labelledby={`reason-${reason}-label`}
                          aria-describedby={`reason-${reason}-explanation`}
                        />
                        <label htmlFor={`reason-${reason}`} className="grid cursor-pointer gap-1">
                          <span id={`reason-${reason}-label`} className="font-strong">
                            {reasonCopy[reason].label}
                          </span>
                          <span
                            id={`reason-${reason}-explanation`}
                            className="text-label text-muted-foreground"
                          >
                            {reasonCopy[reason].explanation}
                          </span>
                        </label>
                      </div>
                    ))}
                  </FieldSet>
                </RadioGroup>
                {fieldState.error ? (
                  <FieldError id="reason-error">{fieldState.error.message}</FieldError>
                ) : null}
              </div>
            )}
          />

          {isWrongAmount ? (
            <Controller
              name="expectedAmount"
              control={control}
              render={({ field, fieldState }) => (
                <Field data-invalid={fieldState.invalid}>
                  <FieldLabel htmlFor="expectedAmount">Expected amount</FieldLabel>
                  <div className="flex items-center gap-2">
                    <span aria-hidden="true" className="font-strong">
                      R
                    </span>
                    <Input
                      {...field}
                      id="expectedAmount"
                      type="text"
                      inputMode="decimal"
                      autoComplete="off"
                      aria-invalid={fieldState.invalid}
                      aria-describedby={describedBy(
                        'expectedAmount',
                        showDifference ? 'expectedAmount-disputing' : undefined,
                      )}
                    />
                  </div>
                  {showDifference ? (
                    <FieldDescription
                      id="expectedAmount-disputing"
                      className="text-label font-strong text-heading"
                    >
                      You&apos;re disputing{' '}
                      {formatMoney({
                        amount: disputedAmount(values, row.amount.amount),
                        currency: row.amount.currency,
                      })}
                    </FieldDescription>
                  ) : null}
                  {fieldState.error ? (
                    <FieldError id="expectedAmount-error">{fieldState.error.message}</FieldError>
                  ) : null}
                </Field>
              )}
            />
          ) : null}

          <Controller
            name="note"
            control={control}
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="note">Note (optional)</FieldLabel>
                <Textarea
                  {...field}
                  id="note"
                  aria-invalid={fieldState.invalid}
                  aria-describedby={describedBy('note', 'note-count')}
                />
                <FieldDescription id="note-count">
                  {`${String(noteLength)} / 1,000`}
                </FieldDescription>
                {fieldState.error ? (
                  <FieldError id="note-error">{fieldState.error.message}</FieldError>
                ) : null}
              </Field>
            )}
          />

          <div className="flex flex-col gap-3 border-t border-border pt-6 md:flex-row">
            <Button type="submit" className="w-full md:w-auto">
              Continue
            </Button>
            <Button asChild variant="secondary" className="w-full md:w-auto">
              <Link to="/transactions">Cancel</Link>
            </Button>
          </div>
        </FieldGroup>
      </form>
    </Page>
  );
}
