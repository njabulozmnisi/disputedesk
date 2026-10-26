---
title: DisputeDesk UX
status: approved
updated: 2026-10-26
ticket: ../initiative-disputedesk.md
---

# DisputeDesk UX

Two peer documents. Both win over any mockup on conflict.

- [DESIGN.md](DESIGN.md): how it looks. Tokens (colour, type, spacing, radius), components, the one theme file and how the hosted sign-in pages are themed from it.
- [EXPERIENCE.md](EXPERIENCE.md): how it behaves. Screens and routes, copy, states, linking without disclosure, idempotent submit, accessibility floor, key flows.
- [mockups/index.html](mockups/index.html): static clickable mockups of every screen and key state, built on [mockups/tokens.css](mockups/tokens.css). Open `index.html` in a browser; no build step.

Mockup pages (34): start, sign-in, sign-up, session-expired; link, link-errors, link-otp, link-otp-wrong, link-locked, link-rate-limited; transactions, transactions-after, transactions-loading, transactions-empty, transactions-error; dispute-new, dispute-new-wrong-amount, dispute-review, dispute-review-retry, dispute-already-disputed, dispute-not-disputable; disputes, disputes-empty, dispute-submitted, dispute-under-review, dispute-prioritised, dispute-upheld, dispute-rejected, dispute-withdrawn, dispute-withdraw, dispute-loading, not-found, error; plus index. `app.css` holds the mockup components and uses only tokens from `tokens.css`. `mockups/fonts/` holds the self-hosted Nunito Sans WOFF2 files and their licence (`OFL.txt`). `mockups/screenshots/` holds review captures of the key screens at 360px and 1280px.

Decision log: `.memlog.md`. Open items are tagged `[ASSUMPTION]` in both documents.
