import * as React from 'react';

import { cn } from '@/lib/utils';

function Textarea({ className, ...props }: React.ComponentProps<'textarea'>) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        'min-h-24 w-full rounded-md border-[1.5px] border-input bg-card px-3 py-2 text-body text-card-foreground placeholder:text-muted-foreground aria-invalid:border-destructive',
        className,
      )}
      {...props}
    />
  );
}

export { Textarea };
