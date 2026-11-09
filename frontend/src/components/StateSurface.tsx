import type { ReactNode } from 'react';
import type { LucideIcon } from 'lucide-react';

import { cn } from '@/lib/utils';

/**
 * DESIGN.md, State surface: full-screen and empty states are one flat surface with a 48px icon tile, the caller's
 * heading (h1 for a full-screen state, h2 for an empty one), the sentences and then the one action.
 *
 * The caller passes its own heading element, so a screen that shows several states keeps one heading at one place in
 * the tree (WCAG 2.2 SC 1.3.1, 2.4.3). The detail screen does this: it renders every state through one `StateSurface`
 * and gives an `icon` only for not found. Without an `icon` the surface classes and the tile are not drawn, and
 * `className` styles the section.
 */
export function StateSurface({
  icon: Icon,
  heading,
  className,
  children,
}: {
  icon?: LucideIcon;
  heading: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <section
      className={cn(
        Icon && 'grid justify-items-start gap-3 rounded-lg border border-border bg-card px-6 py-8',
        className,
      )}
    >
      {Icon ? (
        <div
          aria-hidden="true"
          className="mb-1 grid size-12 place-items-center rounded-full bg-muted text-heading"
        >
          <Icon className="size-6" />
        </div>
      ) : null}
      {heading}
      {children}
    </section>
  );
}
