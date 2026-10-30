import { CustomerStatus } from '@/api/generated/model';

import { describeCustomerStatus } from './customerStatus';

// Criterion 1: one mapping, from DESIGN.md's status tone table.
describe('describeCustomerStatus', () => {
  it.each([
    ['submitted', 'Submitted', 'info', '➚'],
    ['underReview', 'Under review', 'info', '◔'],
    ['underReviewPrioritised', 'Under review (prioritised)', 'priority', '▲'],
    ['resolved', 'Resolved', 'neutral', '⚑'],
    ['withdrawn', 'Withdrawn', 'neutral', '↩'],
  ] as const)('maps %s to its label, tone and icon', (status, label, tone, icon) => {
    expect(describeCustomerStatus(status)).toEqual({ label, tone, icon });
  });

  it('never says "Escalated" for any value of the contract enum', () => {
    for (const status of Object.values(CustomerStatus)) {
      expect(describeCustomerStatus(status)?.label).toBeDefined();
      expect(describeCustomerStatus(status)?.label).not.toMatch(/escalated/i);
    }
  });

  it('gives no badge for a value outside the enum, and never the raw value', () => {
    // An API newer than this build, or a bad response.
    expect(describeCustomerStatus('escalated' as CustomerStatus)).toBeUndefined();
  });
});
