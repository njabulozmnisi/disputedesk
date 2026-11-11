import { zodResolver } from '@hookform/resolvers/zod';
import { Link } from '@tanstack/react-router';
import { REGEXP_ONLY_DIGITS } from 'input-otp';
import { Lock } from 'lucide-react';
import { useRef, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';

import { useVerifyLinking } from '@/api/generated/disputedesk';
import { ApiError } from '@/api/mutator';
import { isProblem, problemInteger, traceIdOf, validationFields } from '@/api/problems';
import { useAnnounce } from '@/app/LiveRegion';
import { usePageHeading } from '@/app/usePageHeading';
import { ErrorSummary } from '@/components/ErrorSummary';
import { Page } from '@/components/Page';
import { ProblemNotice } from '@/components/ProblemNotice';
import { StateSurface } from '@/components/StateSurface';
import { StepProgress } from '@/components/StepProgress';
import { Alert, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field';
import { InputOTP, InputOTPGroup, InputOTPSlot } from '@/components/ui/input-otp';

import { ContactHelp } from './ContactHelp';
import {
  codeFrom,
  codeLength,
  isOverlongCode,
  linkingMessages,
  otpSchema,
  transformPastedCode,
  type OtpValues,
} from './linkSchema';
import { useGoToTransactions } from './useGoToTransactions';
import { useRateLimit } from './useRateLimit';

const cellIndexes = Array.from({ length: codeLength }, (_, index) => index);

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
      <StateSurface
        icon={Lock}
        heading={
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="text-title font-title tracking-title wrap-break-word text-heading"
          >
            We can&apos;t link you online
          </h1>
        }
      >
        <p>Please contact the bank.</p>
        <p>If you think you mistyped your details or the code, you can start again.</p>
        <div className="mt-3">
          <Button asChild variant="secondary">
            <Link to="/link">Start again</Link>
          </Button>
        </div>
      </StateSurface>
      <ContactHelp />
    </Page>
  );
}

function OtpForm({ onLocked }: { onLocked: () => void }) {
  const headingRef = usePageHeading('Enter your OTP');
  const announce = useAnnounce();
  const goToTransactions = useGoToTransactions();
  const noticeRef = useRef<HTMLDivElement>(null);
  const submitRef = useRef<HTMLButtonElement>(null);
  const rateLimit = useRateLimit(noticeRef, submitRef);
  // Set synchronously on submit, because isPending only changes on the next render (as in ReviewScreen).
  const attemptInFlight = useRef(false);
  const [failure, setFailure] = useState<{ traceId: string | undefined }>();
  const [wrongCode, setWrongCode] = useState<{ attemptsRemaining: number | undefined }>();
  // The summary shows after a failed Verify or a returned 400, and takes focus each time. A refused paste is not one:
  // its error shows under the field only, until the next Verify.
  const [summaryRequests, setSummaryRequests] = useState(0);
  const [summaryShown, setSummaryShown] = useState(false);

  const { control, handleSubmit, setError, clearErrors, setValue, setFocus, formState } =
    useForm<OtpValues>({
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
  const mutation = useVerifyLinking({
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
          setSummaryShown(true);
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
      <div className="grid gap-2">
        <StepProgress label="Link your login, step 2 of 2" step={2} />
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-title font-title tracking-title wrap-break-word text-heading"
        >
          Enter your OTP
        </h1>
      </div>

      <p>
        If these details match our records, we&apos;ve sent a code to the cellphone number we hold
        for you.
      </p>

      {summaryShown && codeError ? (
        <ErrorSummary
          items={[{ fieldId: 'code', message: codeError }]}
          requests={summaryRequests}
        />
      ) : null}
      {rateLimit.message ? (
        <Alert ref={noticeRef} tabIndex={-1} tone="rate-limited">
          <AlertTitle>{rateLimit.message}</AlertTitle>
        </Alert>
      ) : null}
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
          setSummaryShown(false);
          void handleSubmit(onValid, () => {
            setSummaryShown(true);
            setSummaryRequests((requests) => requests + 1);
          })(event);
        }}
      >
        <FieldGroup>
          <Controller
            name="code"
            control={control}
            render={({ field, fieldState }) => {
              const invalid = fieldState.invalid || Boolean(wrongCode);
              return (
                <Field data-invalid={invalid}>
                  <FieldLabel htmlFor="code">6-digit one-time code</FieldLabel>
                  {/* Six cells over one real input (input-otp), so paste and one-time-code autofill work. The
                      cells are decoration: the input is the control, and the label and hint describe it. */}
                  <InputOTP
                    {...field}
                    id="code"
                    maxLength={codeLength}
                    pattern={REGEXP_ONLY_DIGITS}
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    pushPasswordManagerStrategy="none"
                    pasteTransformer={transformPastedCode}
                    // A full code ends the error about the last paste or submit, which would sit beside six valid digits.
                    // Not onComplete: input-otp fires it only when the value grows to 6, so an edit of a full field misses it.
                    onChange={(value) => {
                      field.onChange(value);
                      if (value.length === codeLength) clearErrors('code');
                    }}
                    onPaste={(event) => {
                      // input-otp has dropped an over-long paste whole; say why, under the field and to a screen reader
                      // (WCAG 2.2 SC 4.1.3), without the summary or a focus move.
                      if (isOverlongCode(event.clipboardData.getData('text/plain'))) {
                        setError('code', { message: linkingMessages.code });
                        announce(linkingMessages.code);
                      }
                    }}
                    disabled={rateLimit.isWaiting}
                    aria-invalid={invalid}
                    aria-describedby={message ? 'code-hint code-error' : 'code-hint'}
                  >
                    <InputOTPGroup aria-hidden="true">
                      {cellIndexes.map((index) => (
                        <InputOTPSlot key={index} index={index} aria-invalid={invalid} />
                      ))}
                    </InputOTPGroup>
                  </InputOTP>
                  <FieldDescription id="code-hint" className="text-caption">
                    The code expires 5 minutes after we send it. You can paste it, or let your phone
                    fill it in.
                  </FieldDescription>
                  {message ? (
                    // The wrong-code message is an alert; a paste error that replaces it is announced by the live region.
                    <FieldError
                      id="code-error"
                      role={codeError === undefined && wrongCode ? 'alert' : undefined}
                    >
                      {message}
                    </FieldError>
                  ) : null}
                </Field>
              );
            }}
          />

          {/* A rate limit disables the form; a pending request is only aria-disabled, so the button keeps focus. */}
          <Button
            ref={submitRef}
            type="submit"
            disabled={rateLimit.isWaiting}
            aria-disabled={mutation.isPending}
          >
            Verify
          </Button>
        </FieldGroup>
      </form>

      <ContactHelp offerStartAgain />
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
