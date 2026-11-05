import { useEffect, useState } from 'react';

import { onApiResponse } from '@/api/mutator';

import { leaveForSessionEnded } from './sessionEnd';

// The server signs a person out after 15 minutes without an authenticated request (AD-7). Each tab applies the same
// limit itself as a privacy cutoff; the server stays the authority. The warning appears after 13, which leaves 2
// minutes: well over the 20 seconds WCAG 2.2 SC 2.2.1 (Timing Adjustable) asks for.
const idleLimitMs = 15 * 60 * 1000;
const warnAfterIdleMs = 13 * 60 * 1000;

/**
 * The session's idle limit in this tab. While signed in, it ends the session once 15 minutes have passed since the
 * last API response in this tab; tabs do not share a deadline. The deadline is a wall-clock time, checked when the
 * timer fires and when the page becomes visible or gains focus, because a timer drifts across sleep and background
 * throttling (HTML Living Standard, Page Visibility). Returns the warning dialog's open state.
 */
export function useSessionTabs(signedIn: boolean) {
  const [warningOpen, setWarningOpen] = useState(false);

  useEffect(() => {
    if (!signedIn) return;
    let deadline = Date.now() + idleLimitMs;
    let timer: ReturnType<typeof setTimeout> | undefined;

    function check() {
      clearTimeout(timer);
      const remaining = deadline - Date.now();
      if (remaining <= 0) {
        leaveForSessionEnded();
        return;
      }
      const untilWarning = remaining - (idleLimitMs - warnAfterIdleMs);
      if (untilWarning <= 0) setWarningOpen(true);
      timer = setTimeout(check, untilWarning > 0 ? untilWarning : remaining);
    }

    function moveDeadline() {
      deadline = Date.now() + idleLimitMs;
      check();
    }

    function checkWhenVisible() {
      if (document.visibilityState === 'visible') check();
    }

    // A 401 means the session already ended: the QueryClient has dealt with it. Any other response renewed the
    // server's session; a successful one, such as "Stay signed in" or a tab brought to the front, also closes the
    // warning.
    const stopListening = onApiResponse((status) => {
      if (status === 401) return;
      moveDeadline();
      if (status < 400) setWarningOpen(false);
    });

    document.addEventListener('visibilitychange', checkWhenVisible);
    window.addEventListener('focus', check);
    check();
    return () => {
      clearTimeout(timer);
      stopListening();
      document.removeEventListener('visibilitychange', checkWhenVisible);
      window.removeEventListener('focus', check);
      setWarningOpen(false);
    };
  }, [signedIn]);

  return warningOpen;
}
