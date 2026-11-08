import { render } from '@testing-library/react';
import { createElement } from 'react';
import { Check, Flag, Search, Send, Undo2, X } from 'lucide-react';

import { CustomerStatus, type Outcome } from '@/api/generated/model';

import { describeCustomerStatus } from './customerStatus';

// Criterion 1: one mapping, from DESIGN.md's status tone table.
describe('describeCustomerStatus', () => {
  it.each([
    ['submitted', 'Submitted', 'info', Send, 'open'],
    ['underReview', 'Under review', 'info', Search, 'open'],
    ['resolved', 'Resolved', 'neutral', Flag, 'closed'],
    ['withdrawn', 'Withdrawn', 'neutral', Undo2, 'closed'],
  ] as const)('maps %s to its label, tone, icon and group', (status, label, tone, icon, group) => {
    expect(describeCustomerStatus(status)).toEqual({ label, tone, icon, group });
  });

  // Lucide has no search-with-arrow icon: the prioritised badge uses the custom SearchPriority (the lens plus the arrow).
  it('maps underReviewPrioritised to the custom SearchPriority icon', () => {
    const description = describeCustomerStatus('underReviewPrioritised');
    const Icon = description?.icon ?? 'span';

    expect(description).toMatchObject({
      label: 'Under review (prioritised)',
      tone: 'priority',
      group: 'open',
    });
    expect(Icon).not.toBe('span');
    expect(Icon).not.toBe(Search);
    const { container } = render(createElement(Icon));
    expect(container.querySelector('svg.lucide-search-priority')).toBeInTheDocument();
    expect(container.querySelector('circle[cx="11"][cy="11"][r="8"]')).toBeInTheDocument();
    expect(container.querySelector('path[d="M11 14V8M8.5 10.5 11 8l2.5 2.5"]')).toBeInTheDocument();
  });

  // Story 3.7 criterion 2: the outcome only changes a resolved dispute.
  it('shows a resolved dispute by its outcome', () => {
    expect(describeCustomerStatus('resolved', 'upheld')).toEqual({
      label: 'Resolved · Upheld',
      tone: 'success',
      icon: Check,
      group: 'closed',
    });
    expect(describeCustomerStatus('resolved', 'rejected')).toEqual({
      label: 'Resolved · Rejected',
      tone: 'danger',
      icon: X,
      group: 'closed',
    });
  });

  it.each([null, undefined, 'overturned' as Outcome])(
    'keeps plain "Resolved" for the outcome %j',
    (outcome) => {
      expect(describeCustomerStatus('resolved', outcome)?.label).toBe('Resolved');
      expect(describeCustomerStatus('resolved', outcome)?.tone).toBe('neutral');
    },
  );

  it('ignores an outcome on a status that is not resolved', () => {
    expect(describeCustomerStatus('withdrawn', 'upheld')?.label).toBe('Withdrawn');
    expect(describeCustomerStatus('submitted', 'rejected')?.label).toBe('Submitted');
  });

  it('never says "Escalated" for any value of the contract enum', () => {
    for (const status of Object.values(CustomerStatus)) {
      expect(describeCustomerStatus(status)?.label).toBeDefined();
      expect(describeCustomerStatus(status)?.label).not.toMatch(/escalated/i);
    }
    expect(describeCustomerStatus('resolved', 'upheld')?.label).not.toMatch(/escalated/i);
  });

  it('gives no badge for a value outside the enum, and never the raw value', () => {
    // An API newer than this build, or a bad response.
    expect(describeCustomerStatus('escalated' as CustomerStatus)).toBeUndefined();
  });
});
