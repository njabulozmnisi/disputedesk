import type { CustomerStatus } from '@/api/generated/model';

export type StatusTone = 'info' | 'priority' | 'neutral';

export type StatusDescription = { label: string; tone: StatusTone; icon: string };

/**
 * The customer-facing label, tone and icon for a dispute status (DESIGN.md, status tone table), or undefined when
 * there is nothing to show. The one place that turns a status into text, so no screen can say "Escalated" (AD-13).
 * The switch is exhaustive: a value added to `CustomerStatus` fails `tsc` at the `never` line until it is handled.
 */
export function describeCustomerStatus(status: CustomerStatus): StatusDescription | undefined {
  switch (status) {
    case 'submitted':
      return { label: 'Submitted', tone: 'info', icon: '➚' };
    case 'underReview':
      return { label: 'Under review', tone: 'info', icon: '◔' };
    case 'underReviewPrioritised':
      return { label: 'Under review (prioritised)', tone: 'priority', icon: '▲' };
    case 'resolved':
      return { label: 'Resolved', tone: 'neutral', icon: '⚑' };
    case 'withdrawn':
      return { label: 'Withdrawn', tone: 'neutral', icon: '↩' };
    default: {
      // A value this SPA build does not know yet (a newer API) shows no badge, never the raw value.
      status satisfies never;
      return undefined;
    }
  }
}
