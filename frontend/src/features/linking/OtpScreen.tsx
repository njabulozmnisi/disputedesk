import { zodResolver } from '@hookform/resolvers/zod';
import { Link } from '@tanstack/react-router';
import { useRef, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';

import { usePostApiV1LinkingVerify } from '@/api/generated/disputedesk';
import { ApiError } from '@/api/mutator';
import { isProblem, problemInteger, traceIdOf, validationFields } from '@/api/problems';
import { useAnnounce } from '@/app/LiveRegion';
import { usePageHeading } from '@/app/usePageHeading';
import { ErrorSummary } from '@/components/ErrorSummary';
import { Page } from '@/components/Page';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

import { ContactHelp } from './ContactHelp';
import { codeFrom, linkingMessages, otpSchema, type OtpValues } from './linkSchema';
import { useGoToTransactions } from './useGoToTransactions';
import { useRateLimit } from './useRateLimit';

/** "That code didn't work. You have 2 tries left." The count shows only when the API sent 1 or 2. */
function wrongCodeCopy(attemptsRemaining: number | undefined): string {
  if (attemptsRemaining === 2) return "That code didn't work. You have 2 tries left.";
  if (attemptsRemaining === 1) return "That code didn't work. You have 1 try left.";
  return "That code didn't work.";
}

/** Every lockout, a SIM-swap failure included, is this one screen (EXPERIENCE.md, Linking without disclosure, 5). */
function LockedState() {
  const headingRef = usePageHeading("We can't link you online");
  return (
    <Page width="form">
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-title font-title tracking-title wrap-break-word text-heading"
      >
        We can&apos;t link you online
      </h1>
      <p>Please contact the bank.</p>
      <p>If you think you mistyped your details or the code, you can start again.</p>
      <div>
        <Button asChild variant="secondary">
          <Link to="/link">Start again</Link>
        </Button>
      </div>
      <ContactHelp />
    </Page>
  );
}

function OtpForm({ onLocked }: { onLocked: () => void }) {
  const headingRef = usePageHeading('Enter your OTP');
  const announce = useAnnounce();
  const goToTransactions = useGoToTransactions();
  const rateLimit = useRateLimit();
  // Set synchronously on submit, because isPending only changes on the next render (as in ReviewScreen).
  const attemptInFlight = useRef(false);
  const [failure, setFailure] = useState<{ traceId: string | undefined }>();
  const [wrongCode, setWrongCode] = useState<{ attemptsRemaining: number | undefined }>();
  // The summary shows after a failed Verify or a returned 400, and takes focus each time.
  const [summaryRequests, setSummaryRequests] = useState(0);

  const { control, handleSubmit, setError, setValue, setFocus, formState } = useForm<OtpValues>({
    resolver: zodResolver(otpSchema),
    defaultValues: { code: '' },
    mode: 'onSubmit',
    // Typing never re-checks the field before the next press (GOV.UK, Error message: validate on submit).
    reValidateMode: 'onSubmit',
    // The error summary owns focus (GOV.UK Error summary).
    shouldFocusError: false,
  });

  // `gcTime: 0` drops the mutation, and the code in its variables, once this screen is gone (TanStack Query,
  // useMutation).
  const mutation = usePostApiV1LinkingVerify({
    mutation: {
      gcTime: 0,
      onSuccess: () => {
        announce('Your login is linked.');
        goToTransactions();
      },
      onError: (error) => {
        if (isProblem(error, 'already-linked')) {
          goToTransactions();
        } else if (isProblem(error, 'linking-locked')) {
          onLocked();
        } else if (error instanceof ApiError && isProblem(error, 'otp-incorrect')) {
          // `attemptsRemaining` is read from this problem only.
          setWrongCode({ attemptsRemaining: problemInteger(error, 'attemptsRemaining') });
          setValue('code', '');
          setFocus('code');
        } else if (error instanceof ApiError && isProblem(error, 'rate-limited')) {
          rateLimit.start(error);
        } else if (
          error instanceof ApiError &&
          isProblem(error, 'validation') &&
          validationFields(error).includes('code')
        ) {
          setError('code', { message: linkingMessages.code });
          setSummaryRequests((requests) => requests + 1);
        } else {
          setFailure({ traceId: traceIdOf(error) });
        }
      },
      onSettled: () => {
        attemptInFlight.current = false;
      },
    },
  });

  function onValid(values: OtpValues) {
    if (attemptInFlight.current || rateLimit.isWaiting) return;
    attemptInFlight.current = true;
    rateLimit.clear();
    setFailure(undefined);
    mutation.mutate({ data: { code: codeFrom(values.code) } });
  }

  const codeError = formState.errors.code?.message;
  const message = codeError ?? (wrongCode ? wrongCodeCopy(wrongCode.attemptsRemaining) : undefined);

  return (
    <Page width="form">
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-title font-title tracking-title wrap-break-word text-heading"
      >
        Enter your OTP
      </h1>

      <div className="grid gap-1">
        <p>
          If these details match our records, we&apos;ve sent a code to the cellphone number we hold
          for you.
        </p>
        <p className="text-label text-muted-foreground">
          The code expires 5 minutes after we send it.
        </p>
      </div>

      {summaryRequests > 0 && codeError ? (
        <ErrorSummary
          items={[{ fieldId: 'code', message: codeError }]}
          requests={summaryRequests}
        />
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
          setWrongCode(undefined);
          void handleSubmit(onValid, () => {
            setSummaryRequests((requests) => requests + 1);
          })(event);
        }}
      >
        <FieldGroup>
          <Controller
            name="code"
            control={control}
            render={({ field, fieldState }) => (
              <Field data-invalid={fieldState.invalid || Boolean(wrongCode)}>
                <FieldLabel htmlFor="code">6-digit one-time code</FieldLabel>
                {/* One plain input, so paste and one-time-code autofill work and nothing hides the focus ring. */}
                <Input
                  {...field}
                  id="code"
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  spellCheck={false}
                  aria-invalid={fieldState.invalid || Boolean(wrongCode)}
                  aria-describedby={message ? 'code-error' : undefined}
                />
                {message ? (
                  <FieldError id="code-error" role={wrongCode ? 'alert' : undefined}>
                    {message}
                  </FieldError>
                ) : null}
              </Field>
            )}
          />

          {/* `aria-disabled`, not `disabled`, so the button keeps focus while it waits. */}
          <Button type="submit" aria-disabled={mutation.isPending || rateLimit.isWaiting}>
            Verify
          </Button>
        </FieldGroup>
      </form>

      <p className="text-caption text-muted-foreground">
        Didn&apos;t get a code? <Link to="/link">Start again</Link>
      </p>
      <ContactHelp />
    </Page>
  );
}

/** Screen 2b: the code (EXPERIENCE.md, 2b). It opens on its own: the server holds the challenge against the login. */
export function OtpScreen() {
  const [locked, setLocked] = useState(false);
  return locked ? (
    <LockedState />
  ) : (
    <OtpForm
      onLocked={() => {
        setLocked(true);
      }}
    />
  );
}
