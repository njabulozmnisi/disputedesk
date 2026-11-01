import { useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';

import { onApiResponse } from '@/api/mutator';

import {
  leaveToStartPage,
  endSession,
  isSignedIn,
  registerChannel,
  sessionChannelName,
} from './sessionEnd';

// The server signs a person out after 15 minutes without an authenticated request (AD-7). The SPA applies the same
// limit itself as a privacy cutoff, in every tab of the session; the server stays the authority. The warning appears
// after 13, which leaves 2 minutes: well over the 20 seconds WCAG 2.2 SC 2.2.1 (Timing Adjustable) asks for.
const idleLimitMs = 15 * 60 * 1000;
const warnAfterIdleMs = 13 * 60 * 1000;

/**
 * The session's tab behaviour, in one place. It opens this tab's `BroadcastChannel` for as long as the app is mounted
 * (the channel carries no customer data; HTML Living Standard, Broadcasting to other browsing contexts). When another
 * tab's session ends, this tab clears too: a tab that is not signed in ignores every message, and a message is never
 * answered. While signed in, it ends the session in this tab once 15 minutes have passed since the last API response
 * in any tab. The deadline is a wall-clock time, checked when the timer fires and when the page becomes visible or
 * gains focus, because a timer drifts across sleep and background throttling (HTML Living Standard, Page Visibility).
 * Each response here is posted as `activity`; `activity` from another tab moves the deadline and closes the warning.
 * Returns the warning dialog's open state.
 */
export function useSessionTabs(signedIn: boolean) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const channelRef = useRef<BroadcastChannel>(null);
  const [warningOpen, setWarningOpen] = useState(false);

  // Declared first, so it runs first: the idle limit below reads the channel this opens.
  useEffect(() => {
    const channel = new BroadcastChannel(sessionChannelName);
    channelRef.current = channel;
    registerChannel(queryClient, channel); // for postToOtherTabs, which the session's other code calls
    channel.onmessage = (event: MessageEvent) => {
      if (!isSignedIn(queryClient)) return;
      if (event.data === 'expired') {
        void endSession(queryClient, router);
      } else if (event.data === 'signed-out') {
        void leaveToStartPage(queryClient, router);
      }
    };
    return () => {
      registerChannel(queryClient, undefined);
      channel.close();
      channelRef.current = null;
    };
  }, [queryClient, router]);

  useEffect(() => {
    const channel = channelRef.current;
    if (!signedIn || !channel) return;
    let deadline = Date.now() + idleLimitMs;
    let timer: ReturnType<typeof setTimeout> | undefined;

    function check() {
      clearTimeout(timer);
      const remaining = deadline - Date.now();
      if (remaining <= 0) {
        void endSession(queryClient, router); // posts nothing: every tab reaches the shared deadline by itself
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

    // A 401 means the session already ended: the QueryClient has dealt with it.
    const stopListening = onApiResponse((status) => {
      if (status === 401) return;
      moveDeadline();
      channel.postMessage('activity');
    });

    function onTabMessage(event: MessageEvent) {
      if (event.data !== 'activity') return;
      moveDeadline();
      setWarningOpen(false);
    }
    channel.addEventListener('message', onTabMessage);

    document.addEventListener('visibilitychange', checkWhenVisible);
    window.addEventListener('focus', check);
    check();
    return () => {
      clearTimeout(timer);
      stopListening();
      channel.removeEventListener('message', onTabMessage);
      document.removeEventListener('visibilitychange', checkWhenVisible);
      window.removeEventListener('focus', check);
      setWarningOpen(false);
    };
  }, [signedIn, queryClient, router]);

  return { warningOpen, setWarningOpen };
}
