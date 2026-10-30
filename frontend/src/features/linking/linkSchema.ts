import { z } from 'zod';

// zod 4 compiles object schemas with `new Function` unless told not to; the gateway's CSP has no 'unsafe-eval' (see
// raiseSchema.ts).
z.config({ jitless: true });

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

export const otpSchema = z.object({
  code: z.string().refine((typed) => /^[0-9]{6}$/.test(codeFrom(typed)), linkingMessages.code),
});
export type OtpValues = z.infer<typeof otpSchema>;
