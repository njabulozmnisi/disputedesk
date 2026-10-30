import { useEffect, useState } from 'react';

import type { ApiError } from '@/api/mutator';

import { retryAfterSeconds, waitCopy } from './retryAfter';

type Limit = { seconds: number | undefined };

/**
 * The state of a 429 on one linking screen. With a usable `Retry-After` the button waits for exactly that many
 * seconds and the notice then goes; without one the notice says "later" and nothing waits. It lives in the screen, so
 * leaving the screen drops it (the server still enforces the limit).
 */
export function useRateLimit() {
  const [limit, setLimit] = useState<Limit>();

  useEffect(() => {
    if (limit?.seconds === undefined) return;
    const timer = setTimeout(() => {
      setLimit(undefined);
    }, limit.seconds * 1000);
    return () => {
      clearTimeout(timer);
    };
  }, [limit]);

  return {
    /** The notice text, or undefined when the screen is not rate limited. */
    message: limit ? waitCopy(limit.seconds) : undefined,
    isWaiting: limit?.seconds !== undefined,
    start: (error: ApiError) => {
      setLimit({ seconds: retryAfterSeconds(error.retryAfter, new Date()) });
    },
    clear: () => {
      setLimit(undefined);
    },
  };
}
