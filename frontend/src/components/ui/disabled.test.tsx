import { render, screen } from '@testing-library/react';

import { Button } from './button';
import { Input } from './input';

// Criterion 6: a disabled control is drawn with the muted fill, the border edge and the muted text, never at reduced
// opacity (HANDOFF 3.9). The styles live here, in the shared components, so every disabled control gets them.
describe('a disabled Button', () => {
  it.each(['default', 'secondary', 'destructive'] as const)(
    'is drawn muted, not faded, in the %s variant',
    (variant) => {
      render(
        <Button variant={variant} disabled>
          Continue
        </Button>,
      );

      const button = screen.getByRole('button', { name: 'Continue' });
      expect(button).toBeDisabled();
      expect(button).toHaveClass(
        'disabled:bg-muted',
        'disabled:text-muted-foreground',
        'disabled:border-border',
        'disabled:cursor-not-allowed',
      );
      expect(button.className).not.toMatch(/(^| )disabled:opacity/);
    },
  );

  it('keeps aria-disabled as the pending look: faded, with the progress cursor', () => {
    render(<Button aria-disabled="true">Verify</Button>);

    expect(screen.getByRole('button', { name: 'Verify' })).toHaveClass(
      'aria-disabled:cursor-progress',
      'aria-disabled:opacity-70',
    );
  });
});

describe('a disabled Input', () => {
  it('is drawn with the muted fill, the border edge and the muted text', () => {
    render(<Input aria-label="ID number" disabled />);

    const input = screen.getByRole('textbox', { name: 'ID number' });
    expect(input).toBeDisabled();
    expect(input).toHaveClass(
      'disabled:bg-muted',
      'disabled:border-border',
      'disabled:text-muted-foreground',
      'disabled:cursor-not-allowed',
    );
    expect(input.className).not.toMatch(/(^| )disabled:opacity/);
  });
});

// WCAG 2.2 SC 1.4.10: with text scaled up a label wraps inside its pill, so a button never widens the page.
describe('a Button label', () => {
  it('may wrap, centred, and its edge is the theme outline width', () => {
    render(<Button>Submit dispute</Button>);

    const button = screen.getByRole('button', { name: 'Submit dispute' });
    expect(button).toHaveClass('text-center', 'border-(length:--outline-width)');
    expect(button.className).not.toMatch(/whitespace-nowrap/);
  });
});
