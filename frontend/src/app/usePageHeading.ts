import { createContext, useContext, useEffect, useRef, type RefObject } from 'react';

/**
 * RootLayout provides it. It holds the h1 of the first screen of a page load: that screen leaves focus alone, so the
 * first Tab reaches the skip link (WCAG technique G1). Every later screen mounts a new h1, so it is a route change
 * and takes focus. A fixed element gives the same answer when an effect runs twice (React StrictMode).
 */
export const FirstHeadingContext = createContext<RefObject<HTMLHeadingElement | null> | undefined>(
  undefined,
);

/**
 * A route change updates the document title and moves focus to the page's h1 (EXPERIENCE.md, Accessibility Floor).
 * The h1 takes the returned ref and `tabIndex={-1}`.
 */
export function usePageHeading(title: string) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const firstHeading = useContext(FirstHeadingContext);
  if (!firstHeading) throw new Error('usePageHeading needs the shell (RootLayout)');

  useEffect(() => {
    document.title = `${title} · DisputeDesk`;
    const heading = headingRef.current;
    firstHeading.current ??= heading;
    if (heading === firstHeading.current) return;
    heading?.focus();
  }, [title, firstHeading]);

  return headingRef;
}
