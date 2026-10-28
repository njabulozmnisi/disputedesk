import { isProblem, traceIdOf } from '@/api/problems';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';
import type { ApiError } from '@/api/mutator';

/**
 * A transactions request failed. A 503 `core-banking-unavailable` says the disputes are still available
 * (EXPERIENCE.md, State Patterns); any other failure is the unexpected-error notice.
 */
export function TransactionsError({
  error,
  onRetry,
}: {
  error: ApiError | TypeError;
  onRetry: () => void;
}) {
  const bankUnavailable = isProblem(error, 'core-banking-unavailable');

  return (
    <div className="grid gap-6">
      {bankUnavailable ? (
        <ProblemNotice
          message="We can't load your transactions right now."
          nextStep="Your disputes are still available."
          traceId={traceIdOf(error)}
        />
      ) : (
        <ProblemNotice
          message="Something went wrong on our side. Try again."
          traceId={traceIdOf(error)}
        />
      )}
      <div className="flex flex-col gap-3 md:flex-row">
        <Button type="button" onClick={onRetry}>
          Try again
        </Button>
        {bankUnavailable ? (
          <Button asChild variant="secondary">
            {/* shortcut: a plain link (a full page load) until the disputes story adds its route */}
            <a href="/disputes">View disputes</a>
          </Button>
        ) : null}
      </div>
    </div>
  );
}
