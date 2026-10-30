# Action not allowed

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/action-not-allowed.md`
- Status: 409

## When it happens

The action is not allowed from the dispute's current status, for example withdrawing a resolved dispute, or another change happened first. `customerStatus` (customer endpoints) or `disputeStatus` (operations endpoints, the internal status in camelCase; `status` is avoided because RFC 9457 §3.1 keeps that member for the HTTP status code) gives the current status.

## What a client should do

Show the current status. For a retried withdraw, a current status of `withdrawn` means the earlier attempt succeeded.
