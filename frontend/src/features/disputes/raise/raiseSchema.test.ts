import { z } from 'zod';

import { makeRow } from '@/test/support';

import {
  buildRequest,
  createRaiseSchema,
  expectedAmountError,
  type RaiseFormValues,
} from './raiseSchema';

const row = makeRow({ amount: { amount: '849.00', currency: 'ZAR' } });
const schema = createRaiseSchema(row.amount.amount);

function valid(overrides: Partial<RaiseFormValues> = {}): RaiseFormValues {
  return { reason: 'unrecognised', note: '', expectedAmount: '', ...overrides };
}

/** The first message for `field`, or undefined when the values are valid for it. */
function messageFor(values: RaiseFormValues, field: keyof RaiseFormValues) {
  const result = schema.safeParse(values);
  return result.error?.issues.find((issue) => issue.path[0] === field)?.message;
}

// Boundaries, CSP: zod must not compile schemas with `new Function`, which the gateway's CSP forbids.
describe('zod configuration', () => {
  it('parses without code generation (jitless)', () => {
    expect(z.core.globalConfig.jitless).toBe(true);
  });
});

// Criterion 2: reason and expected amount.
describe('expected amount', () => {
  const wrong = (expectedAmount: string) => valid({ reason: 'wrongAmount', expectedAmount });

  it.each(['0.00', '0', '80.00', '80.5', '848.99', ' 80.00 '])('accepts %j', (amount) => {
    expect(messageFor(wrong(amount), 'expectedAmount')).toBeUndefined();
  });

  it('refuses the transaction amount and anything above it', () => {
    const tooHigh = 'The expected amount must be less than R849.00.';
    expect(messageFor(wrong('849.00'), 'expectedAmount')).toBe(tooHigh);
    expect(messageFor(wrong('849'), 'expectedAmount')).toBe(tooHigh);
    expect(messageFor(wrong('900.00'), 'expectedAmount')).toBe(tooHigh);
  });

  it('is required for Wrong amount', () => {
    expect(messageFor(wrong(''), 'expectedAmount')).toBe('Enter the amount you expected to pay.');
    expect(messageFor(wrong('   '), 'expectedAmount')).toBe(
      'Enter the amount you expected to pay.',
    );
  });

  it.each(['80.001', '-1', '1e2', 'R80', '80,00', '.5', '80.', '1000000000000000000'])(
    'refuses %j as not a valid amount',
    (amount) => {
      expect(messageFor(wrong(amount), 'expectedAmount')).toBe(
        'Enter an amount like 80.00, with at most 2 decimals.',
      );
    },
  );

  it('is not checked for any other reason', () => {
    expect(messageFor(valid({ expectedAmount: 'abc' }), 'expectedAmount')).toBeUndefined();
  });
});

describe('reason', () => {
  it('is required', () => {
    expect(messageFor(valid({ reason: '' }), 'reason')).toBe(
      "Choose why you're disputing this transaction.",
    );
  });
});

// Criterion 3: the note counts UTF-16 code units after trimming and is never cut.
describe('note', () => {
  const tooLong = 'Your note must be 1,000 characters or fewer.';

  it('accepts exactly 1,000 characters after trimming', () => {
    expect(messageFor(valid({ note: `  ${'a'.repeat(1000)}  ` }), 'note')).toBeUndefined();
  });

  it('refuses 1,001 characters', () => {
    expect(messageFor(valid({ note: 'a'.repeat(1001) }), 'note')).toBe(tooLong);
  });

  it('counts an emoji as two code units, as String.length does', () => {
    expect(messageFor(valid({ note: '😀'.repeat(501) }), 'note')).toBe(tooLong);
    expect(messageFor(valid({ note: '😀'.repeat(500) }), 'note')).toBeUndefined();
  });
});

// Criteria 2 and 5: the frozen request, with the amount as a JSON-safe decimal string.
describe('buildRequest', () => {
  it('takes accountRef and transactionId from the row and trims the note', () => {
    expect(buildRequest(valid({ note: '  never bought this  ' }), row)).toEqual({
      accountRef: 'ACC-1',
      transactionId: 'TXN-1',
      reason: 'unrecognised',
      note: 'never bought this',
      expectedAmount: null,
    });
  });

  it('sends an empty note as null', () => {
    expect(buildRequest(valid({ note: '   ' }), row).note).toBeNull();
  });

  it('sends the expected amount as a trimmed string, never a number', () => {
    const request = buildRequest(valid({ reason: 'wrongAmount', expectedAmount: ' 80.00 ' }), row);

    expect(request.expectedAmount).toBe('80.00');
    expect(JSON.stringify(request)).toContain('"expectedAmount":"80.00"');
  });

  it('sends no expected amount for any other reason', () => {
    expect(buildRequest(valid({ expectedAmount: '80.00' }), row).expectedAmount).toBeNull();
  });
});

describe('expectedAmountError', () => {
  it.each(['0.00', '0', ' 80.5 ', '848.99'])('accepts %j', (amount) => {
    expect(expectedAmountError(amount, '849.00')).toBeUndefined();
  });

  it.each([
    ['', 'Enter the amount you expected to pay.'],
    ['80.001', 'Enter an amount like 80.00, with at most 2 decimals.'],
    ['849.00', 'The expected amount must be less than R849.00.'],
  ])('refuses %j', (amount, message) => {
    expect(expectedAmountError(amount, '849.00')).toBe(message);
  });
});
