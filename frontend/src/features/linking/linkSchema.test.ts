import {
  codeFrom,
  isOverlongCode,
  linkingMessages,
  otpSchema,
  startSchema,
  transformPastedCode,
} from './linkSchema';

// 12 digits plus a Luhn check digit (ISO/IEC 7812-1, Annex B).
const validId = '8001015009087';
const validAccount = '1234567890';

const startErrors = (idNumber: string, accountNumber: string) => {
  const result = startSchema.safeParse({ idNumber, accountNumber });
  if (result.success) return {};
  const fields: Record<string, string> = {};
  for (const issue of result.error.issues) fields[String(issue.path[0])] = issue.message;
  return fields;
};

// Criterion 2: the bounds of the details form, each side.
describe('the ID number', () => {
  it('accepts 13 digits with a correct Luhn check digit', () => {
    expect(startErrors(validId, validAccount)).toEqual({});
  });

  it.each([
    ['empty', ''],
    ['12 digits', validId.slice(0, 12)],
    ['14 digits', `${validId}0`],
    ['a wrong check digit', `${validId.slice(0, 12)}8`],
    ['a leading space', ` ${validId}`],
    ['a trailing space', `${validId} `],
    ['a space inside', `${validId.slice(0, 6)} ${validId.slice(6)}`],
    ['a letter', `${validId.slice(0, 12)}a`],
    ['an Arabic-Indic digit', `${validId.slice(0, 12)}٣`],
    ['a full-width digit', `${validId.slice(0, 12)}９`],
  ])('refuses %s', (_name, value) => {
    expect(startErrors(value, validAccount).idNumber).toBe(linkingMessages.idNumber);
  });
});

describe('the account number', () => {
  it.each(['123456', '1234567890123456'])('accepts %j (6 and 16 digits)', (value) => {
    expect(startErrors(validId, value)).toEqual({});
  });

  it.each([
    ['empty', ''],
    ['5 digits', '12345'],
    ['17 digits', '12345678901234567'],
    ['a space', '123 456'],
    ['a letter', '12345a'],
    ['a trailing space', '123456 '],
  ])('refuses %s', (_name, value) => {
    expect(startErrors(validId, value).accountNumber).toBe(linkingMessages.accountNumber);
  });
});

// Criterion 4: the code is 6 digits once spaces are removed.
describe('the code', () => {
  it.each(['481516', '481 516', '000123', ' 4 8 1 5 1 6 '])('accepts %j', (value) => {
    expect(otpSchema.safeParse({ code: value }).success).toBe(true);
  });

  it.each(['', '48151', '4815167', '48151a', '481-516', '48151٣', '48 1516'])(
    'refuses %j',
    (value) => {
      expect(otpSchema.safeParse({ code: value }).success).toBe(false);
    },
  );

  it('sends the code with spaces removed and leading zeros kept', () => {
    expect(codeFrom('000 123')).toBe('000123');
  });
});

// Criterion 3: a pasted code longer than 6 digits is refused, never truncated (decision 2026-11-08). One condition
// feeds both the transformer and the onPaste handler, so the two cannot disagree.
describe('a pasted code', () => {
  it.each([
    '4815167',
    '4815 1678',
    ' 4 8 1 5 1 6 7 ',
    '0000000000000000',
    '4815167\n',
    '4815167\r\n',
    '4815167\t',
    '481516 7',
    '4815167a',
    '481-5167',
  ])('is over-long when %j holds more than 6 digits, whatever else it holds', (pasted) => {
    expect(isOverlongCode(pasted)).toBe(true);
  });

  it.each([
    '',
    '481516',
    '481 516',
    '48151',
    '48151a',
    '481516a',
    '481516\n',
    'Your code is 481516',
    '١٢٣٤٥٦٧',
  ])('is not over-long when it is %j', (pasted) => {
    expect(isOverlongCode(pasted)).toBe(false);
  });

  it.each([
    ['481516', '481516'],
    ['481 516', '481516'],
    ['48151a', '48151a'],
    ['Your code is 481516', 'Yourcodeis481516'],
  ])('passes %j on with its spaces removed as %j', (pasted, transformed) => {
    expect(transformPastedCode(pasted)).toBe(transformed);
  });

  it.each(['4815167', '4815 1678', '4815167\n', '4815167a'])(
    'turns %j into one space, which the digits-only pattern refuses',
    (pasted) => {
      expect(transformPastedCode(pasted)).toBe(' ');
      expect(/^\d+$/.test(transformPastedCode(pasted))).toBe(false);
    },
  );
});
