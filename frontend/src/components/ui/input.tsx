import * as React from 'react';

import { cn } from '@/lib/utils';

// The field border is --input (3:1 contrast, DESIGN.md); an invalid field takes the destructive colour and a disabled
// one the muted fill, border and text (HANDOFF 3.9).
function Input({ className, type, ...props }: React.ComponentProps<'input'>) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(
        'min-h-12 w-full min-w-0 rounded-md border-(length:--outline-width) border-input bg-card px-3 text-body text-card-foreground placeholder:text-muted-foreground aria-invalid:border-destructive disabled:cursor-not-allowed disabled:border-border disabled:bg-muted disabled:text-muted-foreground',
        className,
      )}
      {...props}
    />
  );
}

export { Input };
