# Not signed in

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/unauthorized.md`
- Status: 401

## When it happens

No valid session cookie (customer endpoints) or bearer token (operations endpoints). Bearer responses carry `WWW-Authenticate: Bearer` (RFC 6750 §3).

## What a client should do

Sign in again, then retry.
