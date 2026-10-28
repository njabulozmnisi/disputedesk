# Already disputed

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/already-disputed.md`
- Status: 409

## When it happens

The transaction already has a dispute. A transaction can be disputed only once, ever.

## What a client should do

Show the existing dispute instead of raising a new one. The body carries it as two extension members (RFC 9457 §3.2): `disputeId`, the existing dispute's ID, and `customerStatus`, its status as the customer sees it.
