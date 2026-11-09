import { useEffect, useRef, useState, type RefObject } from 'react';

import type { ApiError } from '@/api/mutator';

import { retryAfterSeconds, waitCopy } from './retryAfter';

type Limit = { seconds: number | undefined };

/**
 * The state of a 429 on one linking screen. With a usable `Retry-After` the form is disabled for exactly that many
 * seconds and the notice then goes; without one the notice says "later" and nothing waits. It lives in the screen, so
 * leaving the screen drops it (the server still enforces the limit).
 *
 * Focus is never lost (WCAG 2.2 SC 2.4.3). A disabled control cannot keep focus, so when a wait starts focus moves to
 * the notice (`noticeRef`, which the screen gives `tabIndex={-1}`). When the wait ends the notice unmounts: if it still
 * had focus, focus moves to the submit button (`submitRef`); if the person had moved it elsewhere, it stays there. The
 * screen owns both refs and puts them on its elements.
 */
export function useRateLimit(
  noticeRef: RefObject<HTMLDivElement | null>,
  submitRef: RefObject<HTMLButtonElement | null>,
) {
  const [limit, setLimit] = useState<Limit>();
  const noticeHadFocusAtEnd = useRef(false);
  const isWaiting = limit?.seconds !== undefined;

  useEffect(() => {
    if (limit?.seconds === undefined) return;
    const timer = setTimeout(() => {
      noticeHadFocusAtEnd.current = document.activeElement === noticeRef.current;
      setLimit(undefined);
    }, limit.seconds * 1000);
    return () => {
      clearTimeout(timer);
    };
  }, [limit, noticeRef]);

  // Runs after the render that shows or removes the notice, so the notice exists and the submit button is enabled.
  useEffect(() => {
    if (isWaiting) {
      noticeRef.current?.focus();
    } else if (noticeHadFocusAtEnd.current) {
      noticeHadFocusAtEnd.current = false;
      submitRef.current?.focus();
    }
  }, [isWaiting, noticeRef, submitRef]);

  return {
    /** The notice text, or undefined when the screen is not rate limited. */
    message: limit ? waitCopy(limit.seconds) : undefined,
    isWaiting,
    start: (error: ApiError) => {
      setLimit({ seconds: retryAfterSeconds(error.retryAfter, new Date()) });
    },
    clear: () => {
      setLimit(undefined);
    },
  };
}
