import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';

import { cn } from '@/lib/utils';

// shadcn/ui Badge with the DisputeDesk status tones (DESIGN.md, Status badge): a pill, label type, tone background
// and text from the `status-*` tokens.
const badgeVariants = cva(
  'inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-label font-strong whitespace-nowrap',
  {
    variants: {
      tone: {
        info: 'bg-status-info-bg text-status-info-fg',
        priority: 'bg-status-priority-bg text-status-priority-fg',
        neutral: 'bg-status-neutral-bg text-status-neutral-fg',
      },
    },
  },
);

function Badge({
  className,
  tone,
  ...props
}: React.ComponentProps<'span'> & VariantProps<typeof badgeVariants>) {
  return <span data-slot="badge" className={cn(badgeVariants({ tone }), className)} {...props} />;
}

export { Badge };
