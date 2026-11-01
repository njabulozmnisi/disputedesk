import { createContext, useContext, useEffect, useRef, type RefObject } from 'react';

/**
 * RootLayout provides it. It holds only whether a screen of this page load has shown its h1: the first screen leaves
 * focus alone, so the first Tab reaches the skip link (WCAG technique G1); every later screen is a route change and
 * takes focus. It never holds the element, so a detached first screen can be garbage collected (ASVS 5.0 V14.3.1).
 */
export const FirstHeadingContext = createContext<RefObject<boolean> | undefined>(undefined);

/**
 * A route change updates the document title and moves focus to the page's h1 (EXPERIENCE.md, Accessibility Floor).
 * The h1 takes the returned ref and `tabIndex={-1}`.
 */
export function usePageHeading(title: string) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const headingSeenRef = useContext(FirstHeadingContext);
  // Kept per screen, so an effect that runs twice (React StrictMode) gives the same answer.
  const isFirstScreen = useRef(false);
  if (!headingSeenRef) throw new Error('usePageHeading needs the shell (RootLayout)');

  useEffect(() => {
    document.title = `${title} · DisputeDesk`;
    if (!headingSeenRef.current) {
      headingSeenRef.current = true;
      isFirstScreen.current = true;
    }
    if (isFirstScreen.current) return;
    headingRef.current?.focus();
  }, [title, headingSeenRef]);

  return headingRef;
}
