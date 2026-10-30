import * as React from 'react';
import { RadioGroup as RadioGroupPrimitive } from 'radix-ui';

import { cn } from '@/lib/utils';

function RadioGroup({
  className,
  ...props
}: React.ComponentProps<typeof RadioGroupPrimitive.Root>) {
  return (
    <RadioGroupPrimitive.Root
      data-slot="radio-group"
      className={cn('grid gap-3', className)}
      {...props}
    />
  );
}

function RadioGroupItem({
  className,
  ...props
}: React.ComponentProps<typeof RadioGroupPrimitive.Item>) {
  return (
    <RadioGroupPrimitive.Item
      data-slot="radio-group-item"
      className={cn(
        'flex size-6 shrink-0 cursor-pointer items-center justify-center rounded-full border-[1.5px] border-input bg-card',
        className,
      )}
      {...props}
    >
      <RadioGroupPrimitive.Indicator
        data-slot="radio-group-indicator"
        className="size-3 rounded-full bg-primary"
      />
    </RadioGroupPrimitive.Item>
  );
}

export { RadioGroup, RadioGroupItem };
