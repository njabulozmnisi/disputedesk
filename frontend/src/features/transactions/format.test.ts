import {
  formatDay,
  formatDayWithYear,
  formatFullDay,
  formatMoney,
  formatTimelineTime,
} from './format';

describe('formatMoney', () => {
  it.each([
    ['0.05', 'R0.05'],
    ['849.00', 'R849.00'],
    ['1249.00', 'R1\u202F249.00'],
    ['18450.00', 'R18\u202F450.00'],
    ['1234567.89', 'R1\u202F234\u202F567.89'],
  ])('shows %s as %s', (amount, shown) => {
    expect(formatMoney({ amount, currency: 'ZAR' })).toBe(shown);
  });

  it('does not round the way a float would: the decimal string is formatted as it is', () => {
    expect(formatMoney({ amount: '9007199254740993.10', currency: 'ZAR' })).toBe(
      'R9\u202F007\u202F199\u202F254\u202F740\u202F993.10',
    );
  });

  it.each(['849', '849.5', '-849.00', '1,249.00', 'abc', ''])(
    'refuses %j, which is not a two-decimal amount',
    (amount) => {
      expect(() => formatMoney({ amount, currency: 'ZAR' })).toThrow();
    },
  );
});

describe('formatDay', () => {
  it('writes the day without a leading zero and the month in three letters, as EXPERIENCE.md does', () => {
    expect(formatDay('2026-10-04T10:00:00Z')).toBe('Sun 4 Oct');
    expect(formatDay('2026-09-28T10:00:00Z')).toBe('Mon 28 Sep');
  });

  it('names the day in South African time (UTC+2), not in UTC', () => {
    expect(formatDay('2026-10-26T21:59:59Z')).toBe('Mon 26 Oct');
    expect(formatDay('2026-10-26T22:00:00Z')).toBe('Tue 27 Oct');
  });
});

describe('formatFullDay', () => {
  it('adds the year, as the review screen shows it: "Mon 26 Oct 2026"', () => {
    expect(formatFullDay('2026-10-26T09:14:00Z')).toBe('Mon 26 Oct 2026');
  });

  it('names the day in South African time', () => {
    expect(formatFullDay('2026-12-31T22:00:00Z')).toBe('Fri 1 Jan 2027');
  });
});

describe('formatDayWithYear', () => {
  it('shows the day without the weekday, as the Disputes list does: "26 Oct 2026"', () => {
    expect(formatDayWithYear('2026-10-26T09:14:00Z')).toBe('26 Oct 2026');
  });

  it('names the day in South African time', () => {
    expect(formatDayWithYear('2026-12-31T22:00:00Z')).toBe('1 Jan 2027');
  });
});

describe('formatTimelineTime', () => {
  it('reads like "26 Oct 2026, 14:32 SAST" in South African time', () => {
    expect(formatTimelineTime('2026-10-26T12:32:00Z')).toBe('26 Oct 2026, 14:32 SAST');
  });

  it('writes September as "Sep", never "Sept"', () => {
    expect(formatTimelineTime('2026-09-04T07:05:00Z')).toBe('4 Sep 2026, 09:05 SAST');
  });

  it('uses a 24-hour clock, so midnight is 00:00', () => {
    expect(formatTimelineTime('2026-10-26T22:00:00Z')).toBe('27 Oct 2026, 00:00 SAST');
  });
});
