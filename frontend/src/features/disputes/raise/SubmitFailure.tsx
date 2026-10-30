import { Link } from '@tanstack/react-router';

import { ApiError } from '@/api/mutator';
import { isProblem, problemExtension, traceIdOf } from '@/api/problems';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';

/** How a submit that did not succeed ends (criteria 6 and 8). */
type Failure =
  | { kind: 'unconfirmed' }
  | { kind: 'bankUnavailable' }
  | { kind: 'alreadyDisputed'; disputeId: string | undefined }
  | { kind: 'notDisputable'; reason: string | undefined }
  | { kind: 'notFound' }
  | { kind: 'mismatch' }
  | { kind: 'unexpected' };

function classify(error: ApiError | TypeError): Failure {
  // No response, a gateway timeout, or an attempt still in progress after the last resend: the dispute may exist.
  if (error instanceof TypeError || error.status === 504) return { kind: 'unconfirmed' };
  if (isProblem(error, 'idempotency-in-progress')) return { kind: 'unconfirmed' };
  if (isProblem(error, 'core-banking-unavailable')) return { kind: 'bankUnavailable' };
  if (isProblem(error, 'already-disputed')) {
    return { kind: 'alreadyDisputed', disputeId: problemExtension(error, 'disputeId') };
  }
  if (isProblem(error, 'not-disputable')) {
    return { kind: 'notDisputable', reason: problemExtension(error, 'reason') };
  }
  if (isProblem(error, 'not-found')) return { kind: 'notFound' };
  if (isProblem(error, 'idempotency-mismatch')) return { kind: 'mismatch' };
  return { kind: 'unexpected' };
}

/** A refusal ends this draft: the screen shows the notice instead of the review card. */
export function endsTheDraft(error: ApiError | TypeError): boolean {
  const { kind } = classify(error);
  return (
    kind === 'alreadyDisputed' ||
    kind === 'notDisputable' ||
    kind === 'notFound' ||
    kind === 'mismatch'
  );
}

function notDisputableNextStep(reason: string | undefined): string | undefined {
  if (reason === 'outsideWindow') {
    return "It's now older than 90 days. Contact the bank about this transaction.";
  }
  if (reason === 'notEligible') return 'Contact the bank about this transaction.';
  return undefined;
}

const unexpectedMessage = 'Something went wrong on our side. Try again.';

/**
 * The notice for a failed submit: only the copy deck's sentences and the `traceId`, never the body's title, detail or
 * raw text (AD-3). Try again is in the review screen's button row, not here.
 */
export function SubmitFailure({ error }: { error: ApiError | TypeError }) {
  const failure = classify(error);
  const traceId = traceIdOf(error);
  const backToTransactions = (
    <Button asChild variant="secondary">
      <Link to="/transactions">Back to transactions</Link>
    </Button>
  );

  switch (failure.kind) {
    case 'unconfirmed':
      return (
        <ProblemNotice
          message="We couldn't confirm your dispute was received. Try again. You won't create a second dispute."
          traceId={traceId}
        />
      );
    case 'bankUnavailable':
      return (
        <ProblemNotice
          message="We can't check this transaction with the bank right now. Try again."
          traceId={traceId}
        />
      );
    case 'unexpected':
      return <ProblemNotice message={unexpectedMessage} traceId={traceId} />;
    case 'alreadyDisputed':
      return (
        <div className="grid gap-4">
          <ProblemNotice
            message="This transaction already has a dispute. You can only dispute a transaction once."
            traceId={traceId}
          />
          <div className="flex flex-wrap gap-3">
            {failure.disputeId ? (
              <Button asChild>
                {/* shortcut: a plain link (a full page load) until story 3.7 adds the dispute route */}
                <a href={`/disputes/${encodeURIComponent(failure.disputeId)}`}>View dispute</a>
              </Button>
            ) : null}
            {backToTransactions}
          </div>
        </div>
      );
    case 'notDisputable':
      return (
        <div className="grid gap-4">
          <ProblemNotice
            message="This transaction can't be disputed any more."
            nextStep={notDisputableNextStep(failure.reason)}
            traceId={traceId}
          />
          <div>{backToTransactions}</div>
        </div>
      );
    case 'notFound':
      return (
        <div className="grid gap-4">
          <ProblemNotice message="This transaction is no longer available." traceId={traceId} />
          <div>{backToTransactions}</div>
        </div>
      );
    case 'mismatch':
      return (
        <div className="grid gap-4">
          <ProblemNotice message={unexpectedMessage} traceId={traceId} />
          <div>{backToTransactions}</div>
        </div>
      );
  }
}
