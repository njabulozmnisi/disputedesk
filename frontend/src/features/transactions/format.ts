import type { Money, TransactionResponse } from '@/api/generated/model';

// South African time, with no daylight saving (AD-4). The locale only names the parts: en-ZA would give "04" and
// "Sept", where EXPERIENCE.md shows "4 Oct" and the parts are put in its order below.
const dayFormat = new Intl.DateTimeFormat('en-US', {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
  timeZone: 'Africa/Johannesburg',
});

const yearFormat = new Intl.DateTimeFormat('en-US', {
  year: 'numeric',
  timeZone: 'Africa/Johannesburg',
});

/** "Mon 26 Oct": the day the transaction was made, in South African time. */
export function formatDay(isoDateTime: string): string {
  const parts = dayFormat.formatToParts(new Date(isoDateTime));
  const part = (type: string) => parts.find((candidate) => candidate.type === type)?.value ?? '';
  return `${part('weekday')} ${part('day')} ${part('month')}`;
}

/** "Mon 26 Oct 2026": the same day with its year, for the raise and review screens. */
export function formatFullDay(isoDateTime: string): string {
  return `${formatDay(isoDateTime)} ${yearFormat.format(new Date(isoDateTime))}`;
}

function isDecimalString(value: string): value is `${number}` {
  return /^\d+\.\d{2}$/.test(value);
}

const thousandsSeparator = '\u202F'; // narrow no-break space, so an amount never wraps (EXPERIENCE.md, Responsive)

/**
 * "R1 249.00". `Intl.NumberFormat` takes the API's decimal string as it is, so no float is ever involved (AD-4).
 * The locale gives the point decimal and the symbol; the separator and the missing space are EXPERIENCE.md's.
 */
export function formatMoney(money: Money): string {
  if (!isDecimalString(money.amount))
    throw new Error('A money amount is a decimal string with two decimals');
  const format = new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: money.currency,
    currencyDisplay: 'narrowSymbol',
  });
  return format
    .formatToParts(money.amount)
    .map((part) => {
      if (part.type === 'group') return thousandsSeparator;
      if (part.type === 'literal') return '';
      return part.value;
    })
    .join('');
}

/** What a screen reader says for the amount: "R849.00 out", "R1 200.00 in". */
export function describeAmount(row: TransactionResponse): string {
  const money = formatMoney(row.amount);
  return `${money} ${row.direction === 'credit' ? 'in' : 'out'}`;
}

/** The amount as shown: a credit has a leading plus. */
export function showAmount(row: TransactionResponse): string {
  const money = formatMoney(row.amount);
  return row.direction === 'credit' ? `+${money}` : money;
}
