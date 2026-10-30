import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { Slot } from 'radix-ui';

import { cn } from '@/lib/utils';

// shadcn/ui Button with the DisputeDesk brand layer (DESIGN.md, Components): every button is a pill, at least 48px
// tall; primary is filled, secondary is outlined. `compact` is the 44px pill used in the header.
const buttonVariants = cva(
  'inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 rounded-full border-[1.5px] border-transparent font-strong whitespace-nowrap no-underline transition-colors aria-disabled:cursor-progress aria-disabled:opacity-70',
  {
    variants: {
      variant: {
        default:
          'bg-primary text-primary-foreground hover:bg-primary-hover hover:text-primary-foreground active:bg-primary-pressed',
        secondary:
          'border-primary bg-card text-primary hover:border-primary-hover hover:bg-primary-subtle hover:text-primary-hover active:border-primary-pressed active:text-primary-pressed',
        destructive:
          'bg-destructive text-destructive-foreground hover:bg-destructive/90 hover:text-destructive-foreground',
      },
      size: {
        default: 'min-h-12 px-6 text-body',
        compact: 'min-h-(--tap-target) px-4 text-body',
      },
    },
    defaultVariants: { variant: 'default', size: 'default' },
  },
);

function Button({
  className,
  variant,
  size,
  asChild = false,
  ...props
}: React.ComponentProps<'button'> & VariantProps<typeof buttonVariants> & { asChild?: boolean }) {
  const Component = asChild ? Slot.Root : 'button';
  return (
    <Component
      data-slot="button"
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
    />
  );
}

export { Button };
