import * as React from 'react';
import { OTPInput, OTPInputContext } from 'input-otp';

import { cn } from '@/lib/utils';

// shadcn/ui Input OTP (Manual installation: the CLI adds a `cn` package, so the component is copied with `cn` from
// `@/lib/utils`), changed for DisputeDesk (DESIGN.md, OTP input):
// - six separate cells, `rounded-md`, 48px wide at most on a phone and 56px from 768px, with 24/800 digits;
// - the active cell has a 2px `ring` border and a 4px `primary-subtle` halo; an invalid cell has the `destructive`
//   border, from the `aria-invalid` the caller puts on each slot;
// - a disabled input draws the `muted` fill, `border` edge and `muted-foreground` text, never reduced opacity, through
//   the container's `group` and `group-has-disabled:`;
// - the fake caret stops blinking under `prefers-reduced-motion` (WCAG 2.2 SC 2.3.3);
// - `InputOTPSeparator` is dropped: nothing uses it.
function InputOTP({
  className,
  containerClassName,
  ...props
}: React.ComponentProps<typeof OTPInput> & {
  containerClassName?: string;
}) {
  return (
    <OTPInput
      data-slot="input-otp"
      containerClassName={cn('group flex items-center', containerClassName)}
      className={cn('disabled:cursor-not-allowed', className)}
      {...props}
    />
  );
}

function InputOTPGroup({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="input-otp-group"
      className={cn('flex w-full items-center gap-2', className)}
      {...props}
    />
  );
}

// `text-heading` is the colour utility, so the 24px digit size is the theme variable.
function InputOTPSlot({
  index,
  className,
  ...props
}: React.ComponentProps<'div'> & {
  index: number;
}) {
  const inputOTPContext = React.useContext(OTPInputContext);
  const { char, hasFakeCaret, isActive } = inputOTPContext.slots[index] ?? {};

  return (
    <div
      data-slot="input-otp-slot"
      data-active={isActive}
      className={cn(
        'relative flex h-14 max-w-12 flex-1 items-center justify-center rounded-md border border-input bg-card text-(length:--text-heading) font-title text-card-foreground tabular-nums md:h-16 md:max-w-14',
        // The active border is 1px thicker; the negative margin keeps the cells' widths equal (flex-basis 0 counts borders).
        'data-[active=true]:-m-px data-[active=true]:border-2 data-[active=true]:border-ring data-[active=true]:ring-4 data-[active=true]:ring-primary-subtle',
        'aria-invalid:border-destructive data-[active=true]:aria-invalid:border-destructive data-[active=true]:aria-invalid:ring-destructive/20',
        'group-has-disabled:border-border group-has-disabled:bg-muted group-has-disabled:text-muted-foreground',
        className,
      )}
      {...props}
    >
      {char}
      {hasFakeCaret && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <div className="h-4 w-px animate-caret-blink bg-foreground motion-reduce:animate-none" />
        </div>
      )}
    </div>
  );
}

export { InputOTP, InputOTPGroup, InputOTPSlot };
