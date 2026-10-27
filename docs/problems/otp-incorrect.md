# Code incorrect

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/otp-incorrect.md`
- Status: 422 (AD-3 `BusinessRule`: the request is well formed but the code is wrong; RFC 9110 §15.5.21)

## When it happens

The one-time code is wrong, expired or has no active challenge; all three answer the same way (OWASP uniform responses). `attemptsRemaining` says how many tries are left.

## What a client should do

Let the person try again or request a new code.
