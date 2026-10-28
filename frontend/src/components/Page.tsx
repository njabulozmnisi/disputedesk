import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

/** The page column: lists at 880px, forms and message screens at 560px (DESIGN.md, Layout & Spacing). */
export function Page({
  width = 'content',
  children,
}: {
  width?: 'content' | 'form';
  children: ReactNode;
}) {
  return (
    <div
      className={cn(
        'mx-auto grid w-full gap-6 px-4 pt-6 pb-12 md:gap-8 md:px-6 md:pt-12 md:pb-16',
        width === 'content' ? 'max-w-(--content-max)' : 'max-w-(--form-max)',
      )}
    >
      {children}
    </div>
  );
}
