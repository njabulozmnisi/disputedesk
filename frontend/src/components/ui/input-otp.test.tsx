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
    expect(first).toHaveClass('text-h2');
    expect(first).toHaveClass('md:max-w-14');
  });

  it('gives the active cell a 2px ring edge and a 4px primary-subtle halo, with every cell the same width', () => {
    const { container } = renderCells();
    act(() => {
      container.querySelector('input')?.focus();
    });

    const active = slots(container).filter((cell) => cell.getAttribute('data-active') === 'true');
    expect(active).toHaveLength(1);
    // The border stays 1px on every cell, so the cells keep one width (flex-basis 0 counts borders); the second pixel
    // of the edge is an inset ring (Tailwind, box-shadow: inset-ring).
    expect(active[0]).toHaveClass(
      'data-[active=true]:border-ring',
      'data-[active=true]:inset-ring',
      'data-[active=true]:inset-ring-ring',
      'data-[active=true]:ring-4',
      'data-[active=true]:ring-primary-subtle',
    );
    expect(active[0]?.className).not.toMatch(/-m-px|border-2/);
  });

  it('takes the destructive border from the aria-invalid on a cell', () => {
    const { container } = renderCells();

    const [first] = slots(container);
    expect(first).toHaveClass('aria-invalid:border-destructive');
    expect(first).toHaveClass(
      'data-[active=true]:aria-invalid:border-destructive',
      'data-[active=true]:aria-invalid:inset-ring-destructive',
    );
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
