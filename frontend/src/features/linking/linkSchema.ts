import { z } from 'zod';

export const linkingMessages = {
  idNumber: 'Enter a valid 13-digit ID number.',
  accountNumber: 'Enter a valid account number of 6 to 16 digits.',
  code: 'Enter the 6-digit code.',
};

/** The Luhn check (ISO/IEC 7812-1, Annex B): from the right, double every second digit; the sum ends in 0. */
function passesLuhn(digits: string): boolean {
  let sum = 0;
  for (let index = 0; index < digits.length; index++) {
    let digit = Number(digits[digits.length - 1 - index]);
    if (index % 2 === 1) {
      digit *= 2;
      if (digit > 9) digit -= 9;
    }
    sum += digit;
  }
  return sum % 10 === 0;
}

/** Nothing is trimmed or normalised: a space or a non-ASCII digit makes the value invalid. */
export const startSchema = z.object({
  idNumber: z
    .string()
    .regex(/^[0-9]{13}$/, linkingMessages.idNumber)
    .refine(passesLuhn, linkingMessages.idNumber),
  accountNumber: z.string().regex(/^[0-9]{6,16}$/, linkingMessages.accountNumber),
});
export type StartValues = z.infer<typeof startSchema>;

/** An SMS code is often pasted as "481 516", so spaces (and only spaces) are removed. */
export function codeFrom(typed: string): string {
  return typed.replaceAll(' ', '');
}

/** The six cells of the code input, and the length the server expects. */
export const codeLength = 6;

/**
 * A paste that holds more than 6 digits, whatever else it holds, cannot sit in six cells: input-otp would cut it to its
 * first six. It is refused, never truncated, so the cells never show a code the person did not have and Verify never
 * spends an attempt on one. A paste with exactly 6 digits and other characters ("481516\n") is accepted.
 */
export function isOverlongCode(pasted: string): boolean {
  const digitCount = (pasted.match(/[0-9]/g) ?? []).length;
  return digitCount > codeLength;
}

/**
 * input-otp's `pasteTransformer`: it has no option to refuse a paste, but it drops a paste whole when the result fails
 * its `pattern`, so an over-long code becomes a space, which `REGEXP_ONLY_DIGITS` refuses.
 */
export function transformPastedCode(pasted: string): string {
  return isOverlongCode(pasted) ? ' ' : codeFrom(pasted);
}

export const otpSchema = z.object({
  code: z.string().refine((typed) => /^[0-9]{6}$/.test(codeFrom(typed)), linkingMessages.code),
});
export type OtpValues = z.infer<typeof otpSchema>;
