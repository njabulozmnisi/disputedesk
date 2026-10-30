import type { DisputeAction, DisputeResponse, Outcome, TimelineActor } from '@/api/generated/model';
import { formatTimelineTime } from '@/features/transactions/format';

import { describeCustomerStatus } from '../customerStatus';

/** The upheld or rejected line of a resolve; an unknown or missing outcome has none (AD-13). */
function resolveLine(outcome: Outcome | null): string | undefined {
  if (!outcome) return undefined;
  switch (outcome) {
    case 'upheld':
      return 'The bank agreed with you.';
    case 'rejected':
      return "The bank didn't uphold your dispute.";
    default: {
      outcome satisfies never;
      return undefined;
    }
  }
}

/** The copy deck's line for an action; an action this build does not know has none, never the raw value. */
function describeAction(action: DisputeAction, outcome: Outcome | null): string | undefined {
  switch (action) {
    case 'raise':
      return 'You submitted this dispute.';
    case 'startReview':
      return 'The bank started reviewing your dispute.';
    case 'escalate':
      return 'The bank prioritised the review of your dispute.';
    case 'resolve':
      return resolveLine(outcome);
    case 'withdraw':
      return 'You withdrew this dispute.';
    default: {
      action satisfies never;
      return undefined;
    }
  }
}

function describeActor(actor: TimelineActor): string | undefined {
  switch (actor) {
    case 'you':
      return 'You';
    case 'theBank':
      return 'The bank';
    default: {
      actor satisfies never;
      return undefined;
    }
  }
}

/**
 * The dispute's history as the customer sees it, oldest first as the API sends it. The resolve entry is labelled with
 * the dispute's outcome. An open dispute ends with the promise of more.
 */
export function Timeline({ dispute }: { dispute: DisputeResponse }) {
  const isOpen = describeCustomerStatus(dispute.customerStatus, dispute.outcome)?.group === 'open';
  return (
    <section className="grid gap-4" aria-labelledby="timeline-heading">
      <h2 id="timeline-heading" className="text-subheading font-strong">
        Timeline
      </h2>
      <ol className="grid gap-0 border-l-2 border-border pl-6">
        {dispute.timeline.map((entry, index) => {
          const outcome = entry.action === 'resolve' ? dispute.outcome : null;
          const status = describeCustomerStatus(entry.customerStatus, outcome);
          const actor = describeActor(entry.actor);
          const line = describeAction(entry.action, dispute.outcome);
          return (
            // The list is never reordered, so the index is a stable key.
            <li key={index} className="grid gap-1 pb-6">
              {status ? <p className="font-strong">{status.label}</p> : null}
              {line ? <p>{line}</p> : null}
              {entry.note ? <p className="wrap-break-word">{`“${entry.note}”`}</p> : null}
              <p className="text-caption text-muted-foreground">
                {actor ? `${actor} · ` : null}
                <time dateTime={entry.occurredAt}>{formatTimelineTime(entry.occurredAt)}</time>
              </p>
            </li>
          );
        })}
      </ol>
      {isOpen ? (
        <p className="text-caption text-muted-foreground">
          We&apos;ll update the timeline as your dispute moves.
        </p>
      ) : null}
    </section>
  );
}
