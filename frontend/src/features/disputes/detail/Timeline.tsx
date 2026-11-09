import type { DisputeAction, DisputeResponse, Outcome, TimelineActor } from '@/api/generated/model';
import { formatTimelineTime } from '@/features/transactions/format';

import { describeCustomerStatus } from '../customerStatus';
import { surfaceClass } from './surfaceClass';

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

// The dots sit on the rail, which is drawn per entry: from the dot down into the next entry.
const earlierDotClass =
  'absolute top-1.75 left-0.5 size-3 rounded-full border-2 border-input bg-card';
const latestDotClass =
  'absolute top-1 left-0 size-4 rounded-full border-2 border-primary bg-primary ring-4 ring-primary-subtle';

/**
 * The dispute's history as the customer sees it, oldest first as the API sends it, on a flat surface (DESIGN.md,
 * Timeline). Each entry reads: its status as the customer sees it (PRD FR-17) with the copy deck's sentence beside it,
 * the actor and the time, then any note. The latest entry is marked. An open dispute ends with the promise of more.
 */
export function Timeline({ dispute }: { dispute: DisputeResponse }) {
  const isOpen = describeCustomerStatus(dispute.customerStatus, dispute.outcome)?.group === 'open';
  const latestIndex = dispute.timeline.length - 1;
  return (
    <section className={surfaceClass} aria-labelledby="timeline-heading">
      <h2 id="timeline-heading" className="text-subheading font-strong">
        Timeline
      </h2>
      <ol className="grid">
        {dispute.timeline.map((entry, index) => {
          const isLatest = index === latestIndex;
          const outcome = entry.action === 'resolve' ? dispute.outcome : null;
          const status = describeCustomerStatus(entry.customerStatus, outcome);
          const actor = describeActor(entry.actor);
          const line = describeAction(entry.action, dispute.outcome);
          return (
            // The list is never reordered, so the index is a stable key.
            <li key={index} className="relative grid gap-1 pb-6 pl-8 last:pb-0">
              {isLatest ? null : (
                <span
                  aria-hidden="true"
                  data-slot="timeline-rail"
                  className="absolute top-3 -bottom-2 left-1.75 w-0.5 bg-border"
                />
              )}
              <span
                aria-hidden="true"
                data-slot="timeline-dot"
                className={isLatest ? latestDotClass : earlierDotClass}
              />
              {status || line ? (
                <p className="flex flex-wrap items-baseline gap-x-2">
                  {status ? (
                    <span className="text-label font-strong text-muted-foreground">
                      {status.label}
                    </span>
                  ) : null}
                  {line ? (
                    <span className={isLatest ? 'font-title text-heading' : 'font-label'}>
                      {line}
                    </span>
                  ) : null}
                </p>
              ) : null}
              <p className="flex flex-wrap items-baseline gap-x-2 text-caption text-muted-foreground">
                {actor ? <span className="font-strong text-foreground">{actor}</span> : null}
                <time dateTime={entry.occurredAt}>{formatTimelineTime(entry.occurredAt)}</time>
              </p>
              {entry.note ? (
                <p className="mt-1 rounded-md bg-muted px-4 py-3 text-label wrap-anywhere">{`“${entry.note}”`}</p>
              ) : null}
            </li>
          );
        })}
      </ol>
      {isOpen ? (
        <p className="border-t border-border pt-4 text-caption text-muted-foreground">
          We&apos;ll update the timeline as your dispute moves.
        </p>
      ) : null}
    </section>
  );
}
