import { render, screen, within } from '@testing-library/react';

import { expectDecorativeIcon } from '@/test/icons';

import { ErrorSummary } from './ErrorSummary';

const items = [{ fieldId: 'code', message: 'Enter the 6-digit code.' }];

// Criterion 3: the summary is drawn in the danger Alert's look, and keeps the GOV.UK error summary's structure.
describe('ErrorSummary', () => {
  it('leads with the danger icon', () => {
    render(<ErrorSummary items={items} requests={1} />);

    const summary = screen.getByRole('group', { name: 'Check the details below' });
    expectDecorativeIcon(summary.querySelector('svg.lucide-triangle-alert'));
  });

  it('keeps its focused group, its inner alert, its h2 title and its links', () => {
    render(<ErrorSummary items={items} requests={1} />);

    const summary = screen.getByRole('group', { name: 'Check the details below' });
    expect(summary).toHaveFocus();
    expect(summary).toHaveAttribute('tabindex', '-1');
    expect(summary).toHaveAttribute('aria-labelledby', 'error-summary-title');
    const alert = within(summary).getByRole('alert');
    expect(
      within(alert).getByRole('heading', { level: 2, name: 'Check the details below' }),
    ).toBeInTheDocument();
    expect(within(alert).getByRole('link', { name: 'Enter the 6-digit code.' })).toHaveAttribute(
      'href',
      '#code',
    );
  });

  it('is the only alert role, so nothing is announced twice', () => {
    render(<ErrorSummary items={items} requests={1} />);

    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });
});
