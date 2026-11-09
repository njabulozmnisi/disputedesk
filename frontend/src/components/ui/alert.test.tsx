import { render, screen } from '@testing-library/react';

import { expectDecorativeIcon } from '@/test/icons';

import { Alert, AlertDescription, AlertTitle } from './alert';

// Criterion 1: each tone leads with its icon, and only danger and rate limited are announced as alerts (WAI-ARIA APG,
// Alert pattern). Colours and radius are QA's visual checks: jsdom has no layout.
describe('Alert', () => {
  it.each([
    ['danger', 'lucide-triangle-alert', 'alert'],
    ['rate-limited', 'lucide-clock', 'alert'],
    ['info', 'lucide-info', null],
    ['success', 'lucide-circle-check', null],
    ['neutral', 'lucide-info', null],
  ] as const)('the %s tone shows %s and has the role %s', (tone, iconClass, role) => {
    const { container } = render(
      <Alert tone={tone}>
        <AlertTitle>A title</AlertTitle>
      </Alert>,
    );

    expectDecorativeIcon(container.querySelector(`svg.${iconClass}`));
    const alert = container.querySelector('[data-slot="alert"]');
    if (role) expect(alert).toHaveAttribute('role', role);
    else expect(alert).not.toHaveAttribute('role');
  });

  it('puts the icon first, before the words', () => {
    const { container } = render(
      <Alert tone="info">
        <AlertTitle>A title</AlertTitle>
      </Alert>,
    );

    expect(container.querySelector('[data-slot="alert"]')?.firstElementChild).toHaveClass(
      'lucide-info',
    );
  });

  it('lets the title wrap: it has no line clamp', () => {
    const longTitle =
      'This transaction already has a dispute. You can only dispute a transaction once, and this sentence is long on purpose.';
    render(
      <Alert tone="danger">
        <AlertTitle>{longTitle}</AlertTitle>
        <AlertDescription>The description</AlertDescription>
      </Alert>,
    );

    const title = screen.getByText(longTitle);
    expect(title.className).not.toMatch(/line-clamp/);
  });

  it('draws the description in the tone colour, not the muted text colour', () => {
    render(
      <Alert tone="danger">
        <AlertDescription>The description</AlertDescription>
      </Alert>,
    );

    expect(screen.getByText('The description').className).not.toMatch(/text-muted-foreground/);
  });

  it.each([
    ['danger', 'bg-status-danger-bg', 'text-status-danger-fg'],
    ['rate-limited', 'bg-status-danger-bg', 'text-status-danger-fg'],
    ['info', 'bg-status-info-bg', 'text-status-info-fg'],
    ['success', 'bg-status-success-bg', 'text-status-success-fg'],
    ['neutral', 'bg-status-neutral-bg', 'text-status-neutral-fg'],
  ] as const)('the %s tone uses the theme status tokens', (tone, background, foreground) => {
    const { container } = render(<Alert tone={tone}>Words</Alert>);

    const alert = container.querySelector('[data-slot="alert"]');
    expect(alert).toHaveClass(background, foreground, 'rounded-md', 'p-4');
    expect(alert?.className).not.toMatch(/\bborder\b|text-sm|font-medium/);
  });

  it('lets a caller set its own role, as the error summary does', () => {
    const { container } = render(
      <Alert tone="danger" role="group">
        Words
      </Alert>,
    );

    expect(container.querySelector('[data-slot="alert"]')).toHaveAttribute('role', 'group');
  });
});
