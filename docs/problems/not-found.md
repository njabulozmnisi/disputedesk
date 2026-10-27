# Not found

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/not-found.md`
- Status: 404

## When it happens

The resource does not exist, or it belongs to another customer. Both cases give the same answer, so a response never reveals that another customer's resource exists (RFC 9110 §15.5.5).

## What a client should do

Do not retry. Return the person to the list they came from.
