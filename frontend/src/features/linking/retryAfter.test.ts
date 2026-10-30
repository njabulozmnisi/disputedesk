import { retryAfterSeconds, waitCopy } from './retryAfter';

const now = new Date('2026-10-30T10:00:00Z');
const httpDate = (secondsAhead: number) =>
  new Date(now.getTime() + secondsAhead * 1000).toUTCString();

// Criterion 7: Retry-After as delay-seconds or an HTTP-date (RFC 9110 section 10.2.3), usable from 1 to 86 400 seconds.
describe('retryAfterSeconds', () => {
  it.each([
    ['0', undefined],
    ['1', 1],
    ['60', 60],
    ['61', 61],
    ['900', 900],
    ['86400', 86400],
    ['86401', undefined],
  ])('reads delay-seconds %j as %j', (raw, expected) => {
    expect(retryAfterSeconds(raw, now)).toBe(expected);
  });

  it.each([
    [1, 1],
    [720, 720],
    [86400, 86400],
    [86401, undefined],
    [0, undefined],
    [-60, undefined],
  ])('reads an HTTP-date %j seconds ahead as %j', (ahead, expected) => {
    expect(retryAfterSeconds(httpDate(ahead), now)).toBe(expected);
  });

  it.each([null, '', ' ', 'soon', '-5', '1.5', '12 ', '1e3', '٣', 'Mon, 99 Foo 2026 10:00:00 GMT'])(
    'counts %j as no Retry-After',
    (raw) => {
      expect(retryAfterSeconds(raw, now)).toBeUndefined();
    },
  );
});

// The copy rounds up to whole minutes.
describe('waitCopy', () => {
  it.each([
    [1, 'Too many attempts. Try again in 1 minute.'],
    [60, 'Too many attempts. Try again in 1 minute.'],
    [61, 'Too many attempts. Try again in 2 minutes.'],
    [720, 'Too many attempts. Try again in 12 minutes.'],
    [900, 'Too many attempts. Try again in 15 minutes.'],
    [undefined, 'Too many attempts. Try again later.'],
  ])('says %j seconds as %j', (seconds, copy) => {
    expect(waitCopy(seconds)).toBe(copy);
  });
});
