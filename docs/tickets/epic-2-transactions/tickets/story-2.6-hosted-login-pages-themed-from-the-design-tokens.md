---
id: 6
type: story
title: "Hosted login pages themed from the design tokens"
parent: epic-2-transactions
covers: [R1, R32, R37]
after: [4, 2]
hitl: false
risk: low
---

# Hosted login pages themed from the design tokens

## Description

Provisioning sets Zitadel's instance label policy from `frontend/src/theme/tokens.css`, uploads the wordmark and the design system's font, and activates the policy, so the hosted sign-up, sign-in and verification pages match DisputeDesk (FR-1, AD-6). A small generator prints the policy body offline, so CI checks it with no stack and no secret; `provision.sh` applies it idempotently on every `docker compose up`. Email verification and the Mailpit SMTP settings already exist (P0-2) and are not rebuilt here.

## Requirements

Verify: The CI `platform` job runs the generator on `tokens.css` and checks its output and the brand files; `verify.sh` checks the active policy against the generator output; `infra/smoke.sh` checks the hosted login's framing headers; provisioning twice on the running stack is a supervisor check.

Inputs and their bounds (all are repo files at fixed paths, none comes from the environment):

- `tokens.css`: in the first `:root` block (light) and the `:root` block inside `@media (prefers-color-scheme: dark)` (dark), each of `--primary`, `--background`, `--destructive` and `--foreground` is declared exactly once, with a value matching `^#[0-9a-fA-F]{6}$`. A token matched by prefix (`--primary-foreground`, `--primary-hover`) is not that token. Anything else exits non-zero before any API call. Zitadel's limit per colour field is 50 characters.
- The font: `frontend/src/theme/fonts/nunito-sans-latin-400-normal.woff2` (13 KiB), at most 512 KiB, detected by Zitadel as `font/*`.
- The logos: `frontend/src/theme/wordmark.png` and `frontend/src/theme/wordmark-dark.png`, PNG only (never SVG), each at most 512 KiB.

1. **The label-policy body is generated from the tokens, offline** (R1; FR-1)
   **Given** `infra/zitadel/provision/label-policy.sh <tokens.css>`, which reads no `PAT_FILE` and makes no network call
   **When** it runs on the repo's `tokens.css`
   **Then** it prints the `UpdateLabelPolicy` body with `primaryColor` from `--primary`, `backgroundColor` from `--background`, `warnColor` from `--destructive` and `fontColor` from `--foreground`, the same four from the dark block as `primaryColorDark`, `backgroundColorDark`, `warnColorDark` and `fontColorDark`, and `themeMode: THEME_MODE_AUTO`; every other field keeps its default (Zitadel, Branding guide; Admin API v1 `UpdateLabelPolicy`)
   **And** the body is built with `jq -n --arg`, never by joining strings, because Login v2 writes these values into CSS custom properties on the identity origin (jq manual, `--arg`; OWASP ASVS 5.0 V1.2)
   **And** a colour that fails the bound above, or a token found zero times or twice in a block, exits non-zero naming the token
   **And** the CI `platform` job runs it and checks the output with `jq -e` against DESIGN.md's eight hex values, written into the test (compared ignoring case), not re-read from `tokens.css`, which would test the parser against itself

2. **The brand files exist and are safe to upload** (R1; FR-1)
   **Given** the wordmark drawn as in DESIGN.md (Wordmark): "DisputeDesk" in Nunito Sans 800 with a small dot, on a transparent background; light in navy `--heading` #14287a with a cobalt `--primary` #2b57c9 dot, dark in #c9d6ff with a #8caaf7 dot
   **When** the CI `platform` job checks the files named under the bounds
   **Then** both wordmarks begin with the PNG signature (W3C PNG Specification, section 5.2) and the font with the WOFF2 signature `wOF2` (W3C WOFF 2.0, section 3), and each is at most 512 KiB
   **And** `tokens.css` names the two wordmark files in a comment, not in a CSS property the SPA never reads, and the files are committed; nothing is downloaded at image build or provisioning time

3. **The policy is applied idempotently and activated** (R1, R37; FR-1)
   **Given** a stack where provisioning has run zero or more times
   **When** `provision.sh` runs
   **Then** it reads the instance's preview policy (`GET /admin/v1/policies/label/_preview`) and sends `PUT /admin/v1/policies/label` with the generator's body only when one of the nine fields differs (colours compared ignoring case), following the script's look-up-before-create rule
   **And** it uploads the font to `/assets/v1/instance/policy/label/font`, the light wordmark to `.../logo` and the dark wordmark to `.../logo/dark` only when the preview's `fontUrl`, `logoUrl` or `logoUrlDark` is empty, marked `# shortcut: a changed font or logo file needs down -v, upgrade when rebrands are frequent`
   **And** it then calls `POST /admin/v1/policies/label/_activate`, which has no not-changed check
   **And** a second run exits 0 and leaves the active policy unchanged; any HTTP status of 400 or above, a not-changed 400 included, fails the run: no `|| true` and no `ALLOW_CONFLICT`-style exception, so a real rejection never leaves the pages unbranded behind an exit 0 (OWASP ASVS 5.0 V16.5: fail securely)
   **And** it calls only those label-policy and asset endpoints: never the login policy, the SMTP configuration or the security settings (Zitadel Admin API v1 and Assets API)

4. **Uploads keep the PAT off the command line** (R1, R32; FR-1)
   **Given** the multipart upload helper beside `call` in `provision.sh` (curl's documented `-F name=@file`, form field `file`; `call` cannot be reused because it forces a JSON `Content-Type`)
   **When** it uploads a file
   **Then** it sends the PAT only through `-H @"$AUTH_HEADER"`, the existing header file, and reuses the existing IAM_OWNER PAT with no new scope or volume
   **And** it never uses `-v`, `--trace`, `set -x` or `-u`, takes its file paths only from constants in the script (curl reads `;`, `<` and `@` in `-F` values as instructions), and on HTTP 400 or above logs only the method, path, status and response body, as `call` does (curl manual; OWASP ASVS 5.0 V13.3.1, V16.2.5)
   **And** Compose mounts `tokens.css`, the 400 WOFF2 and the two wordmarks into `provision` one file at a time with `:ro`, never the whole `frontend/` directory

5. **The active policy is checked read-only** (R1; FR-1)
   **Given** `infra/zitadel/provision/verify.sh`, which already checks Zitadel facts read-only
   **When** it runs on the stack
   **Then** a new check passes only when `GET /admin/v1/policies/label` (the active policy) has the nine fields equal to the generator's output for the mounted `tokens.css`, and non-empty `fontUrl`, `logoUrl` and `logoUrlDark`

6. **The hosted login is not weakened** (R1, R32; FR-1, NFR-SEC-7)
   **Given** the hosted login after this story
   **When** `infra/smoke.sh` requests `http://auth.localhost:8080/ui/v2/login/loginname`
   **Then** the response still carries `frame-ancestors 'none'` in its `Content-Security-Policy` and `X-Frame-Options: deny`, proving the story changed no framing or embedding setting (OWASP ASVS 5.0 V3.4.6; OWASP Clickjacking Defense Cheat Sheet)

7. **The login waits for the branding** (R1, R37; FR-1)
   **Given** `docker-compose.yml`
   **When** the stack starts
   **Then** `zitadel-login` has `depends_on: provision: { condition: service_completed_successfully }` beside its `zitadel` dependency, so Login v2 never caches the default branding before activation (Docker Compose specification, `depends_on`); it cannot deadlock, because `provision` waits only for `zitadel`
   **And** the rest of the `zitadel-login` environment, `CSP_FETCH_ENABLED` among it, is unchanged

## Boundaries

- Must not change: email verification and the Mailpit SMTP defaults (`EMAIL_VERIFICATION: "true"`, `mailpit:1025`, and `verify.sh`'s SMTP check), delivered by P0-2.
- No CSP or header change: the nginx identity server block gets no `add_header`, and no external font or image host is added. Login v2 sends `font-src` and `img-src` `'self' <ZITADEL_API_URL>`, and `/assets/v1/` is served on the identity origin, so the uploaded files load as `'self'`. If the font fails to load, the fix is never a wider `font-src`.
- One font file: Login v2 injects one `@font-face` with no weight, so text at 600, 700 and 800 on the hosted pages is synthesised bold from the 400 file.
- No icon: Login v2 v4.20 never reads `iconUrl`, so no icon is uploaded.
- The label policy has no radius or spacing fields; the hosted pages keep their own shapes (DESIGN.md, Theme file and hosted-login theming).
- Login v2 caches branding for 1 hour: after a rebrand on a running stack, restart `zitadel-login`.
- Why not the `ZITADEL_DEFAULTINSTANCE_LABELPOLICY_*` environment variables: they would copy the tokens into Compose by hand, apply only when the instance is created, and carry neither files nor `themeMode`.
- The SPA's wordmark stays HTML text and a CSS dot (`Header.tsx`); the PNGs are for the hosted pages only.
- The Dockerfile adds no package: bash, curl and jq are enough; whatever was used to draw the PNGs is not added to the image.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Screenshot the hosted sign-up and sign-in pages, light and dark, beside `docs/ux/mockups/sign-up.html` and `docs/ux/mockups/sign-in.html`: the wordmark, colours and font match, the browser console shows no CSP violation, and the font request returns 200 from `/assets/v1/`.
- Sign up a new login and find the verification email in Mailpit (FR-1's regression proof for P0-2).
- Run provisioning twice: the second run exits 0, makes no `PUT` and no upload, and the active policy is unchanged.
- In a throwaway copy, change one colour token in `tokens.css`: the CI generator test fails. Change `--primary` to `blue`: the generator exits non-zero naming `--primary`.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: M2.
- Packages: none.
- Touches:
  - `infra/zitadel/provision/label-policy.sh` (new: the generator)
  - `infra/zitadel/provision/provision.sh` (label policy section and the upload helper)
  - `infra/zitadel/provision/verify.sh` (the active-policy check)
  - `infra/zitadel/provision/Dockerfile` (`COPY label-policy.sh`)
  - `infra/smoke.sh` (the framing-header check)
  - `docker-compose.yml` (`provision` mounts the four files `:ro`; `zitadel-login` `depends_on: provision`)
  - `.github/workflows/ci.yml` (`platform` job: generator and brand-file checks)
  - `frontend/src/theme/wordmark.png`, `frontend/src/theme/wordmark-dark.png` (new)
  - `frontend/src/theme/tokens.css` (a comment naming the wordmark files; no token changes)

## References

- parent — docs/tickets/epic-2-transactions/epic-2-transactions.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-6
- design — docs/ux/DESIGN.md, Theme file and hosted-login theming; Components, Wordmark
- design — docs/ux/mockups/sign-up.html
- design — docs/ux/mockups/sign-in.html
- Zitadel — https://zitadel.com/docs/guides/manage/customize/branding
- Zitadel — https://zitadel.com/docs/apis/resources/admin
- Zitadel — https://zitadel.com/docs/apis/assets/assets
- curl — https://curl.se/docs/manpage.html
- jq — https://jqlang.org/manual/
- W3C — https://www.w3.org/TR/png-3/
- W3C — https://www.w3.org/TR/WOFF2/
- Docker — https://docs.docker.com/reference/compose-file/services/#depends_on
- OWASP — https://owasp.org/www-project-application-security-verification-standard/
- OWASP — https://cheatsheetseries.owasp.org/cheatsheets/Clickjacking_Defense_Cheat_Sheet.html
- verification — docs/verification/2.6-challenge.md, 2.6-prebuild-review.md, 2.6-prebuild-security.md

## Notes

- Risk low: a wrong theme is visible and recoverable; the security-relevant parts (PAT handling, framing headers, no 400 tolerance) have their own criteria.
- Decision: 2026-10-27 — the hosted login pages also use the design system's font, uploaded to the label policy. Settled 2026-10-30 from the Zitadel v4.20.0 source: the font upload accepts `font/*`, so the 400 WOFF2 is used as it is.
- Decision: 2026-10-30 — the old criterion 2 (email verification and Mailpit SMTP) is dropped, because P0-2 delivered and checks it; the Mailpit email stays a supervisor check (pre-build review).
- Decision: 2026-10-30 — the logos are PNG only: an SVG opened at its asset URL would run any script inside it on the identity origin (pre-build security S3, narrowing the review's "PNG or outlined SVG").
- Decision: 2026-10-30 — one story, not split into "generated body" and "applied idempotently": the generator alone changes nothing a person sees, and both halves touch the same provisioning files, so a split adds a hand-over without making either half clearer. The criteria keep the two halves apart (1 and 2 offline; 3 to 7 on the stack).
- Zitadel facts used here were checked against the v4.20.0 source pinned in `docker-compose.yml` (pre-build review): the v2 settings service cannot write branding, a no-change `UpdateLabelPolicy` returns 400 `Errors.Instance.LabelPolicy.NotChanged`, uploads detect the type from the bytes, and each asset is at most 512 KiB.
