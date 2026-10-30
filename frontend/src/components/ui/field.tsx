import * as React from 'react';

import { Label } from '@/components/ui/label';
import { cn } from '@/lib/utils';

// shadcn/ui Field, trimmed to the parts the forms use. `Field` does not wire `aria-invalid` or `aria-describedby`
// (shadcn/ui, Forms): each form sets them on its control and gives `FieldError` and `FieldDescription` an `id`.

function FieldSet({ className, ...props }: React.ComponentProps<'fieldset'>) {
  return <fieldset data-slot="field-set" className={cn('grid gap-3', className)} {...props} />;
}

function FieldLegend({ className, ...props }: React.ComponentProps<'legend'>) {
  return (
    <legend
      data-slot="field-legend"
      className={cn('mb-3 text-body font-strong', className)}
      {...props}
    />
  );
}

function FieldGroup({ className, ...props }: React.ComponentProps<'div'>) {
  return <div data-slot="field-group" className={cn('grid gap-6', className)} {...props} />;
}

function Field({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      role="group"
      data-slot="field"
      className={cn('grid gap-2 data-[invalid=true]:text-destructive', className)}
      {...props}
    />
  );
}

function FieldLabel({ className, ...props }: React.ComponentProps<typeof Label>) {
  return <Label data-slot="field-label" className={cn('w-fit', className)} {...props} />;
}

function FieldDescription({ className, ...props }: React.ComponentProps<'p'>) {
  return (
    <p
      data-slot="field-description"
      className={cn('text-label text-muted-foreground', className)}
      {...props}
    />
  );
}

// No `role="alert"`: the error summary takes focus when errors appear, so it is the announcement.
function FieldError({ className, ...props }: React.ComponentProps<'p'>) {
  return (
    <p
      data-slot="field-error"
      className={cn('text-label font-label text-destructive', className)}
      {...props}
    />
  );
}

export { Field, FieldDescription, FieldError, FieldGroup, FieldLabel, FieldLegend, FieldSet };
