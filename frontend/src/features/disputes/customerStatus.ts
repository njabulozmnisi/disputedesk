import {
  Check,
  createLucideIcon,
  Flag,
  Search,
  Send,
  Undo2,
  X,
  type LucideIcon,
} from 'lucide-react';

import type { CustomerStatus, Outcome } from '@/api/generated/model';

export type StatusTone = 'info' | 'priority' | 'neutral' | 'success' | 'danger';

export type StatusDescription = {
  label: string;
  tone: StatusTone;
  icon: LucideIcon;
  /** The Disputes screen's section: submitted and under review are open, resolved and withdrawn are closed. */
  group: 'open' | 'closed';
};

// Lucide's Search with an up arrow in the lens (docs/ux/HANDOFF.md, section 2). Lucide has no such icon, so this is its
// documented custom icon (https://lucide.dev/guide/react/advanced/typescript): Search's own nodes plus the arrow.
const SearchPriority = createLucideIcon('SearchPriority', [
  ['circle', { cx: '11', cy: '11', r: '8', key: 'lens' }],
  ['path', { d: 'm21 21-4.34-4.34', key: 'handle' }],
  ['path', { d: 'M11 14V8M8.5 10.5 11 8l2.5 2.5', key: 'arrow' }],
]);

/** A resolved dispute is shown by its outcome; a missing or unknown outcome stays plain "Resolved". */
function describeResolved(outcome: Outcome | null | undefined): StatusDescription {
  const resolved: StatusDescription = {
    label: 'Resolved',
    tone: 'neutral',
    icon: Flag,
    group: 'closed',
  };
  if (!outcome) return resolved;
  switch (outcome) {
    case 'upheld':
      return { label: 'Resolved · Upheld', tone: 'success', icon: Check, group: 'closed' };
    case 'rejected':
      return { label: 'Resolved · Rejected', tone: 'danger', icon: X, group: 'closed' };
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
      return { label: 'Submitted', tone: 'info', icon: Send, group: 'open' };
    case 'underReview':
      return { label: 'Under review', tone: 'info', icon: Search, group: 'open' };
    case 'underReviewPrioritised':
      return {
        label: 'Under review (prioritised)',
        tone: 'priority',
        icon: SearchPriority,
        group: 'open',
      };
    case 'resolved':
      return describeResolved(outcome);
    case 'withdrawn':
      return { label: 'Withdrawn', tone: 'neutral', icon: Undo2, group: 'closed' };
    default: {
      // A value this SPA build does not know yet (a newer API) shows no badge, never the raw value.
      status satisfies never;
      return undefined;
    }
  }
}
