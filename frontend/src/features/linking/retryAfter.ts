const maxWaitSeconds = 86_400;

// shortcut: IMF-fixdate only (RFC 9110 §5.6.7 asks recipients to accept all three HTTP-date forms); the API and gateway send delay-seconds, so accept the obsolete forms if a sender ever uses a date
const imfFixdate = /^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} GMT$/;

/**
 * The wait in seconds that a `Retry-After` value asks for (RFC 9110 section 10.2.3: delay-seconds or an HTTP-date),
 * or undefined when it is missing, unreadable, not ahead of `now` or more than 86 400 seconds ahead.
 */
export function retryAfterSeconds(raw: string | null, now: Date): number | undefined {
  if (raw === null) return undefined;
  let seconds: number;
  if (/^[0-9]+$/.test(raw)) {
    seconds = Number(raw);
  } else if (imfFixdate.test(raw)) {
    seconds = Math.ceil((Date.parse(raw) - now.getTime()) / 1000);
  } else {
    return undefined;
  }
  return seconds >= 1 && seconds <= maxWaitSeconds ? seconds : undefined;
}

/** The notice for a 429: the wait rounded up to whole minutes, or "later" when there is no wait to report. */
export function waitCopy(seconds: number | undefined): string {
  if (seconds === undefined) return 'Too many attempts. Try again later.';
  const minutes = Math.ceil(seconds / 60);
  return `Too many attempts. Try again in ${String(minutes)} ${minutes === 1 ? 'minute' : 'minutes'}.`;
}
