import type { CustomerStatus, Outcome } from '@/api/generated/model';

export type StatusTone = 'info' | 'priority' | 'neutral' | 'success' | 'danger';

export type StatusDescription = {
  label: string;
  tone: StatusTone;
  icon: string;
  /** The Disputes screen's section: submitted and under review are open, resolved and withdrawn are closed. */
  group: 'open' | 'closed';
};

/** A resolved dispute is shown by its outcome; a missing or unknown outcome stays plain "Resolved". */
function describeResolved(outcome: Outcome | null | undefined): StatusDescription {
  const resolved: StatusDescription = {
    label: 'Resolved',
    tone: 'neutral',
    icon: '⚑',
    group: 'closed',
  };
  if (!outcome) return resolved;
  switch (outcome) {
    case 'upheld':
      return { label: 'Resolved · Upheld', tone: 'success', icon: '✓', group: 'closed' };
    case 'rejected':
      return { label: 'Resolved · Rejected', tone: 'danger', icon: '✕', group: 'closed' };
    default: {
      // An outcome this SPA build does not know yet (a newer API) stays plain "Resolved".
      outcome satisfies never;
      return resolved;
    }
  }
}

/**
 * The customer-facing label, tone, icon and group for a dispute status (DESIGN.md, status tone table), or undefined
 * when there is nothing to show. The one place that turns a status into text, so no screen can say "Escalated"
 * (AD-13). Both switches are exhaustive: a value added to `CustomerStatus` or `Outcome` fails `tsc` at the `never`
 * line until it is handled. The outcome only matters when the status is `resolved`.
 */
export function describeCustomerStatus(
  status: CustomerStatus,
  outcome?: Outcome | null,
): StatusDescription | undefined {
  switch (status) {
    case 'submitted':
      return { label: 'Submitted', tone: 'info', icon: '➚', group: 'open' };
    case 'underReview':
      return { label: 'Under review', tone: 'info', icon: '◔', group: 'open' };
    case 'underReviewPrioritised':
      return { label: 'Under review (prioritised)', tone: 'priority', icon: '▲', group: 'open' };
    case 'resolved':
      return describeResolved(outcome);
    case 'withdrawn':
      return { label: 'Withdrawn', tone: 'neutral', icon: '↩', group: 'closed' };
    default: {
      // A value this SPA build does not know yet (a newer API) shows no badge, never the raw value.
      status satisfies never;
      return undefined;
    }
  }
}
