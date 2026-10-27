# Code incorrect

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/otp-incorrect.md`
- Status: decided by story 6.2 (an AD-3 error kind)

## When it happens

The one-time code is wrong, expired or has no active challenge; all three answer the same way (OWASP uniform responses). `attemptsRemaining` says how many tries are left.

## What a client should do

Let the person try again or request a new code.
