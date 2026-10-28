import { useEffect, useRef } from 'react';

/**
 * A route change updates the document title and moves focus to the page's h1 (EXPERIENCE.md, Accessibility Floor).
 * The h1 takes the returned ref and `tabIndex={-1}`.
 */
export function usePageHeading(title: string) {
  const headingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    document.title = `${title} · DisputeDesk`;
    headingRef.current?.focus();
  }, [title]);

  return headingRef;
}
