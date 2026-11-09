import * as React from 'react';
import { cva } from 'class-variance-authority';
import { CircleCheck, Clock, Info, TriangleAlert, type LucideIcon } from 'lucide-react';

import { cn } from '@/lib/utils';

// shadcn/ui Alert (Manual installation: the registry's CLI output imports `cn` from a new `cn` package, so the
// component is copied with `cn` from `@/lib/utils`), changed for DisputeDesk (DESIGN.md, Notices):
// - one `tone` instead of `variant`, with the status tokens, `rounded-md`, no border, `p-4` and the theme's type tokens;
// - the tone's icon is the first child, 16px;
// - `role="alert"` only on the danger and rate-limited tones, because only errors announce (WAI-ARIA APG, Alert
//   pattern; EXPERIENCE.md, Accessibility Floor). A caller's `role` wins, as the registry's `...props` order lets it;
// - the title has no line clamp, so it wraps at 320px (WCAG 2.2 SC 1.4.10), and the description takes the tone colour.
const alertVariants = cva(
  'relative grid w-full grid-cols-[auto_1fr] items-start gap-x-3 gap-y-1 rounded-md p-4 text-body',
  {
    variants: {
      tone: {
        danger: 'bg-status-danger-bg text-status-danger-fg',
        'rate-limited': 'bg-status-danger-bg text-status-danger-fg',
        info: 'bg-status-info-bg text-status-info-fg',
        success: 'bg-status-success-bg text-status-success-fg',
        neutral: 'bg-status-neutral-bg text-status-neutral-fg text-label',
      },
    },
  },
);

type AlertTone = 'danger' | 'rate-limited' | 'info' | 'success' | 'neutral';

const toneIcons: Record<AlertTone, LucideIcon> = {
  danger: TriangleAlert,
  'rate-limited': Clock,
  info: Info,
  success: CircleCheck,
  neutral: Info,
};

const announcedTones: AlertTone[] = ['danger', 'rate-limited'];

function Alert({
  className,
  tone,
  children,
  ...props
}: React.ComponentProps<'div'> & { tone: AlertTone }) {
  const Icon = toneIcons[tone];
  return (
    <div
      data-slot="alert"
      role={announcedTones.includes(tone) ? 'alert' : undefined}
      className={cn(alertVariants({ tone }), className)}
      {...props}
    >
      <Icon aria-hidden="true" className="mt-1 size-4 shrink-0" />
      {children}
    </div>
  );
}

function AlertTitle({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div data-slot="alert-title" className={cn('col-start-2 font-strong', className)} {...props} />
  );
}

function AlertDescription({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="alert-description"
      className={cn('col-start-2 grid justify-items-start gap-1', className)}
      {...props}
    />
  );
}

export { Alert, AlertDescription, AlertTitle };
