import { z } from 'zod';

import { Reason } from '@/api/generated/model';
import type { RaiseDisputeRequest, TransactionResponse } from '@/api/generated/model';
import { formatMoney } from '@/features/transactions/format';

import { centsToDecimal, toCents } from './cents';

// zod 4 compiles object schemas with `new Function` unless told not to (zod 4.6.7, core/schemas.js and core/util.js
// `allowsEval`). The gateway's CSP has no 'unsafe-eval' (story 2.5), so zod parses without it.
z.config({ jitless: true });

export const noteLimit = 1000;

/** What the form holds: strings only, with '' for "no reason chosen yet". */
export type RaiseFormValues = { reason: Reason | ''; note: string; expectedAmount: string };

export const emptyValues: RaiseFormValues = { reason: '', note: '', expectedAmount: '' };

export const messages = {
  reasonMissing: "Choose why you're disputing this transaction.",
  amountMissing: 'Enter the amount you expected to pay.',
  amountInvalid: 'Enter an amount like 80.00, with at most 2 decimals.',
  noteTooLong: 'Your note must be 1,000 characters or fewer.',
};

// The API's pattern for `expectedAmount` (backend/openapi.json).
const amountPattern = /^[0-9]{1,17}(\.[0-9]{1,2})?$/;

/** The line for an API 400 on `expectedAmount`: its one key covers four rules, so the line names those a customer can break. */
export function serverAmountMessage(transactionAmount: string): string {
  return `Enter an amount less than ${formatMoney({ amount: transactionAmount, currency: 'ZAR' })}, with at most 2 decimals.`;
}

/** The message for a Wrong amount expected amount that breaks a rule, or undefined when it is valid. */
export function expectedAmountError(
  expectedAmount: string,
  transactionAmount: string,
): string | undefined {
  const amount = expectedAmount.trim();
  if (amount === '') return messages.amountMissing;
  if (!amountPattern.test(amount)) return messages.amountInvalid;
  if (toCents(amount) >= toCents(transactionAmount)) {
    return `The expected amount must be less than ${formatMoney({ amount: transactionAmount, currency: 'ZAR' })}.`;
  }
  return undefined;
}

/** The rules for one transaction: the expected amount must be below that transaction's amount. */
export function createRaiseSchema(transactionAmount: string) {
  return z
    .object({
      reason: z.union([z.enum(Reason), z.literal('')]).refine((reason) => reason !== '', {
        error: messages.reasonMissing,
      }),
      note: z.string().refine((note) => note.trim().length <= noteLimit, {
        error: messages.noteTooLong,
      }),
      expectedAmount: z.string(),
    })
    .superRefine(({ reason, expectedAmount }, context) => {
      if (reason !== Reason.wrongAmount) return;
      const message = expectedAmountError(expectedAmount, transactionAmount);
      if (message) context.addIssue({ code: 'custom', path: ['expectedAmount'], message });
    });
}

/** The one request for a draft: built once from the row and the validated values, then never changed. */
export function buildRequest(
  values: RaiseFormValues,
  row: TransactionResponse,
): RaiseDisputeRequest {
  const note = values.note.trim();
  const expectedAmount = values.expectedAmount.trim();
  return {
    accountRef: row.accountRef,
    transactionId: row.transactionId,
    reason: values.reason === '' ? null : values.reason,
    note: note === '' ? null : note,
    expectedAmount: values.reason === Reason.wrongAmount ? expectedAmount : null,
  };
}

/** "You're disputing" for a wrong-amount draft is the difference; for any other reason the whole amount. */
export function disputedAmount(values: RaiseFormValues, transactionAmount: string): string {
  const expected = values.expectedAmount.trim();
  if (values.reason !== Reason.wrongAmount) return transactionAmount;
  return centsToDecimal(toCents(transactionAmount) - toCents(expected));
}
