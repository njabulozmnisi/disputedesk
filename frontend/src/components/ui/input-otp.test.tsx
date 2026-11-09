import { render } from '@testing-library/react';
import { act } from 'react';

import * as inputOtp from './input-otp';
import { InputOTP, InputOTPGroup, InputOTPSlot } from './input-otp';

// Criteria 2 and 4: the cells' look. jsdom has no layout or Tailwind, so these check the classes the handoff names; the
// rendered look is QA's check at 360px and 1280px (and the builder's Chromium pass).
function renderCells(props: { disabled?: boolean } = {}) {
  return render(
    <InputOTP maxLength={6} value="48" onChange={() => undefined} aria-label="Code" {...props}>
      <InputOTPGroup>
        {[0, 1, 2, 3, 4, 5].map((index) => (
          <InputOTPSlot key={index} index={index} />
        ))}
      </InputOTPGroup>
    </InputOTP>,
  );
}

const slots = (container: HTMLElement) =>
  Array.from(container.querySelectorAll('[data-slot="input-otp-slot"]'));

describe('InputOTP', () => {
  it('exports the three parts the code form uses and no separator', () => {
    expect(Object.keys(inputOtp).sort()).toEqual(['InputOTP', 'InputOTPGroup', 'InputOTPSlot']);
  });

  it('draws each cell rounded-md, 48px wide at most on a phone, with 24/800 digits from theme tokens', () => {
    const { container } = renderCells();

    const [first] = slots(container);
    expect(first).toHaveClass('rounded-md', 'max-w-12', 'flex-1', 'font-title', 'bg-card');
    expect(first).toHaveClass('text-(length:--text-heading)');
    expect(first).toHaveClass('md:max-w-14');
  });

  it('gives the active cell a 2px ring border and a 4px primary-subtle halo', () => {
    const { container } = renderCells();
    act(() => {
      container.querySelector('input')?.focus();
    });

    const active = slots(container).filter((cell) => cell.getAttribute('data-active') === 'true');
    expect(active).toHaveLength(1);
    // The negative margin keeps the cells' widths equal while the border grows from 1px to 2px.
    expect(active[0]).toHaveClass(
      'data-[active=true]:-m-px',
      'data-[active=true]:border-2',
      'data-[active=true]:border-ring',
      'data-[active=true]:ring-4',
      'data-[active=true]:ring-primary-subtle',
    );
  });

  it('takes the destructive border from the aria-invalid on a cell', () => {
    const { container } = renderCells();

    const [first] = slots(container);
    expect(first).toHaveClass('aria-invalid:border-destructive');
    expect(first).toHaveClass('data-[active=true]:aria-invalid:border-destructive');
  });

  it('stops the fake caret blinking under reduced motion', () => {
    const { container } = renderCells();
    act(() => {
      container.querySelector('input')?.focus();
    });

    const caret = container.querySelector('.animate-caret-blink');
    expect(caret).toBeInTheDocument();
    expect(caret).toHaveClass('motion-reduce:animate-none');
  });

  it('draws a disabled input with the muted fill, border and text through the container, never at reduced opacity', () => {
    const { container } = renderCells({ disabled: true });

    const root = container.querySelector('[data-input-otp-container]');
    expect(root).toHaveClass('group');
    expect(root?.className).not.toMatch(/opacity/);
    const [first] = slots(container);
    expect(first).toHaveClass(
      'group-has-disabled:bg-muted',
      'group-has-disabled:border-border',
      'group-has-disabled:text-muted-foreground',
    );
    expect(first?.className).not.toMatch(/opacity/);
    expect(container.querySelector('input')).toBeDisabled();
  });
});
