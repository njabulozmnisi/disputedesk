import { centsToDecimal, toCents } from './cents';

// Criterion 2: money is compared as integer cents parsed from the decimal strings, never as a float (AD-4).
describe('toCents', () => {
  it.each([
    ['0.00', 0n],
    ['0', 0n],
    ['80', 8000n],
    ['80.5', 8050n],
    ['80.00', 8000n],
    ['848.99', 84899n],
    // 17 digits before the point is past Number.MAX_SAFE_INTEGER once scaled to cents.
    ['99999999999999999.99', 9999999999999999999n],
  ])('reads %s as %s cents', (decimal, cents) => {
    expect(toCents(decimal)).toBe(cents);
  });
});

describe('centsToDecimal', () => {
  it.each([
    [0n, '0.00'],
    [5n, '0.05'],
    [50000n, '500.00'],
    [84899n, '848.99'],
  ])('writes %s cents as %s', (cents, decimal) => {
    expect(centsToDecimal(cents)).toBe(decimal);
  });
});
