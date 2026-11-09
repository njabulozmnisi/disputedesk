import { cn } from '@/lib/utils';

/**
 * DESIGN.md, Step indicator: a text label over a two-segment bar. The label carries the meaning; the bar is decorative
 * (WCAG 2.2 SC 1.1.1). Every use has two steps, so the count is fixed.
 */
export function StepProgress({ label, step }: { label: string; step: 1 | 2 }) {
  return (
    <div className="grid gap-2">
      <p className="text-label font-strong text-muted-foreground">{label}</p>
      <div aria-hidden="true" className="grid grid-cols-2 gap-1">
        <span className="h-1 rounded-full bg-primary" />
        <span className={cn('h-1 rounded-full', step === 2 ? 'bg-primary' : 'bg-border')} />
      </div>
    </div>
  );
}
