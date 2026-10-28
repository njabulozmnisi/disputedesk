import { clsx, type ClassValue } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';

// tailwind-merge must know the theme's own type scale and weights (src/theme/tokens.css). Otherwise it reads
// `text-body` as a colour and drops the real colour class (`text-primary-foreground`) next to it.
const twMerge = extendTailwindMerge({
  extend: {
    classGroups: {
      'font-size': [
        {
          text: [
            'display',
            'display-lg',
            'title',
            'heading',
            'subheading',
            'body',
            'label',
            'caption',
            'amount',
            'amount-hero',
          ],
        },
      ],
      'font-weight': [{ font: ['label', 'strong', 'title'] }],
    },
  },
});

/** shadcn/ui's class-name helper: joins conditional classes and lets a later Tailwind class win. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
