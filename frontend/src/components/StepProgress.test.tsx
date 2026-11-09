import { render, screen } from '@testing-library/react';

import { StepProgress } from './StepProgress';

// Criterion 6: a text label with a decorative two-segment bar (WCAG 2.2 SC 1.1.1). The 4px height and the pill shape
// are QA's visual checks: jsdom has no layout.
function segments(container: HTMLElement) {
  return Array.from(container.querySelectorAll('[aria-hidden="true"] > span'));
}

describe('StepProgress', () => {
  it('shows the label as muted text', () => {
    render(<StepProgress label="Link your login, step 1 of 2" step={1} />);

    expect(screen.getByText('Link your login, step 1 of 2')).toHaveClass(
      'text-label',
      'font-strong',
      'text-muted-foreground',
    );
  });

  it('colours one segment primary and one border at step 1', () => {
    const { container } = render(<StepProgress label="Step 1 of 2" step={1} />);

    const [first, second] = segments(container);
    expect(segments(container)).toHaveLength(2);
    expect(first).toHaveClass('bg-primary');
    expect(second).toHaveClass('bg-border');
    expect(second).not.toHaveClass('bg-primary');
  });

  it('colours both segments primary at step 2', () => {
    const { container } = render(<StepProgress label="Step 2 of 2" step={2} />);

    const [first, second] = segments(container);
    expect(first).toHaveClass('bg-primary');
    expect(second).toHaveClass('bg-primary');
    expect(second).not.toHaveClass('bg-border');
  });

  it('hides the bar from assistive technology and leaves the label as the only content', () => {
    const { container } = render(<StepProgress label="Step 1 of 2" step={1} />);

    expect(container.querySelector('[aria-hidden="true"]')).toBeInTheDocument();
    expect(container).toHaveTextContent('Step 1 of 2');
    expect(container.querySelectorAll('[aria-hidden="true"]')).toHaveLength(1);
  });
});
