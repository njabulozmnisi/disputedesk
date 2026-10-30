/**
 * The decimal string `"80.5"` as integer cents (`8050n`). The input has at most 2 decimals; a `bigint` holds the 17
 * digits the API accepts, which a `Number` would round. Money is never a float (AD-4).
 */
export function toCents(decimal: string): bigint {
  const [whole = '0', fraction = ''] = decimal.split('.');
  return BigInt(whole + fraction.padEnd(2, '0'));
}

/** Integer cents as the API's decimal string: `50000n` is `"500.00"`. */
export function centsToDecimal(cents: bigint): string {
  const digits = cents.toString().padStart(3, '0');
  return `${digits.slice(0, -2)}.${digits.slice(-2)}`;
}
