import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { useRef, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';

import { usePostApiV1LinkingStart } from '@/api/generated/disputedesk';
import { ApiError } from '@/api/mutator';
import { isProblem, traceIdOf, validationFields } from '@/api/problems';
import { usePageHeading } from '@/app/usePageHeading';
import { ErrorSummary } from '@/components/ErrorSummary';
import { Page } from '@/components/Page';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

import { ContactHelp } from './ContactHelp';
import { linkingMessages, startSchema, type StartValues } from './linkSchema';
import { useGoToTransactions } from './useGoToTransactions';
import { useRateLimit } from './useRateLimit';

const fields = ['idNumber', 'accountNumber'] as const;

/** Screen 2a: ID number and account number (EXPERIENCE.md, 2a). Every 202 looks the same (Linking without disclosure). */
export function DetailsScreen() {
  const headingRef = usePageHeading('Link your login');
  const navigate = useNavigate();
  const goToTransactions = useGoToTransactions();
  const rateLimit = useRateLimit();
  // Set synchronously on submit, because isPending only changes on the next render (as in ReviewScreen).
  const attemptInFlight = useRef(false);
  const [failure, setFailure] = useState<{ traceId: string | undefined }>();
  // The summary shows after a failed Continue or a returned 400, and takes focus each time.
  const [summaryRequests, setSummaryRequests] = useState(0);

  const { control, handleSubmit, setError, formState } = useForm<StartValues>({
    resolver: zodResolver(startSchema),
    defaultValues: { idNumber: '', accountNumber: '' },
    mode: 'onSubmit',
    // Typing never re-checks the field before the next press (GOV.UK, Error message: validate on submit).
    reValidateMode: 'onSubmit',
    // The error summary owns focus (GOV.UK Error summary).
    shouldFocusError: false,
  });

  // `gcTime: 0` drops the mutation, and the values in its variables, once this screen is gone (TanStack Query,
  // useMutation).
  const mutation = usePostApiV1LinkingStart({
    mutation: {
      gcTime: 0,
      onSuccess: () => {
        void navigate({ to: '/link/otp' });
      },
      onError: (error) => {
        if (isProblem(error, 'already-linked')) {
          goToTransactions();
        } else if (error instanceof ApiError && isProblem(error, 'rate-limited')) {
          rateLimit.start(error);
        } else if (error instanceof ApiError && isProblem(error, 'validation')) {
          const named = fields.filter((field) => validationFields(error).includes(field));
          for (const field of named) setError(field, { message: linkingMessages[field] });
          if (named.length > 0) setSummaryRequests((requests) => requests + 1);
          else setFailure({ traceId: traceIdOf(error) });
        } else {
          setFailure({ traceId: traceIdOf(error) });
        }
      },
      onSettled: () => {
        attemptInFlight.current = false;
      },
    },
  });

  function onValid(values: StartValues) {
    if (attemptInFlight.current || rateLimit.isWaiting) return;
    attemptInFlight.current = true;
    rateLimit.clear();
    setFailure(undefined);
    mutation.mutate({ data: { idNumber: values.idNumber, accountNumber: values.accountNumber } });
  }

  const summaryItems = fields.flatMap((field) => {
    const message = formState.errors[field]?.message;
    return message ? [{ fieldId: field, message }] : [];
  });

  const describedBy = (field: (typeof fields)[number]) =>
    formState.errors[field] ? `${field}-error` : undefined;

  return (
    <Page width="form">
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-title font-title tracking-title wrap-break-word text-heading"
      >
        Link your login
      </h1>

      {summaryRequests > 0 && summaryItems.length > 0 ? (
        <ErrorSummary items={summaryItems} requests={summaryRequests} />
      ) : null}
      {rateLimit.message ? <ProblemNotice message={rateLimit.message} /> : null}
      {failure ? (
        <ProblemNotice
          message="Something went wrong on our side. Try again."
          traceId={failure.traceId}
        />
      ) : null}

      <form
        noValidate
        onSubmit={(event) => {
          void handleSubmit(onValid, () => {
            setSummaryRequests((requests) => requests + 1);
          })(event);
        }}
      >
        <FieldGroup>
          <Controller
            name="idNumber"
            control={control}
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="idNumber">ID number</FieldLabel>
                <Input
                  {...field}
                  id="idNumber"
                  type="text"
                  inputMode="numeric"
                  autoComplete="off"
                  spellCheck={false}
                  aria-invalid={fieldState.invalid}
                  aria-describedby={describedBy('idNumber')}
                />
                {fieldState.error ? (
                  <FieldError id="idNumber-error">{fieldState.error.message}</FieldError>
                ) : null}
              </Field>
            )}
          />

          <Controller
            name="accountNumber"
            control={control}
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid}>
                <FieldLabel htmlFor="accountNumber">Account number</FieldLabel>
                <Input
                  {...field}
                  id="accountNumber"
                  type="text"
                  inputMode="numeric"
                  autoComplete="off"
                  spellCheck={false}
                  aria-invalid={fieldState.invalid}
                  aria-describedby={describedBy('accountNumber')}
                />
                {fieldState.error ? (
                  <FieldError id="accountNumber-error">{fieldState.error.message}</FieldError>
                ) : null}
              </Field>
            )}
          />

          {/* `aria-disabled`, not `disabled`, so the button keeps focus while it waits. */}
          <Button type="submit" aria-disabled={mutation.isPending || rateLimit.isWaiting}>
            Continue
          </Button>
        </FieldGroup>
      </form>

      <ContactHelp />
    </Page>
  );
}
