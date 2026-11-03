# tf-gateway-spa review (Small tier, ad-011 change 3, infra half)

- Builder: built. `infra/images/gateway/Dockerfile` lost its Node stage and copies `/dist/` from the named context `spa`; its context is `infra/images/gateway/`. Compose: build-only `spa` service under profile `build`, gateway `additional_contexts: { spa: "service:spa" }`. CI publish and security.yml build the SPA image, then the gateway with `--build-context spa=docker-image://...`. Root `.dockerignore` deleted (no root-context build left). `check-compose.sh` gained the spa checks (each fails on its deliberate break); `smoke.sh` clean slate lists images with `--profile build`. Old and new gateway images identical under `/usr/share/nginx/html` (8 files, SHA-256), `/etc/nginx` and image config, telemetry on and off; real stack built `spa` and created no container; smoke OK twice; sign-in through the gateway OK. Full suite 1623 of 1624: `RateLimitWindowTests.Window_PermitSpentInTheFirstSegment_...` (wall-clock window, 429 expected, 400 at 7 s) failed under memory pressure 2 and passed alone; no backend code changed. CI is the authoritative run.
- Verifier: pass on read-only checks (criteria 1-4 and 6; check-compose OK). The Copilot cross-model run was skipped because the dispatch said read-only; criterion 5 rests on the builder's comparison. Countermeasure: say "read-only, with the Copilot run" in verifier dispatches.
- Reviewer: approve. `docker-image://` of a local tag resolves with the default docker driver (local probe; the platform job's `gateway.test.sh` uses the same form before publish, so a runner difference fails `platform` and skips publish). Two nits.
- Fix round 1 (fresh builder): the two comments; actionlint and check-compose pass; supervisor read the delta.
- Supervisor: AD-27 and the source tree updated. `.claude/hooks/guard.py` still lists the deleted root `.dockerignore` (dead entry, batched with the other guard cleanups).
- The builder's two `infra/tests/smoke.sh` runs (`down -v`) reset the shared local stack's volumes; synthetic data, reseeded on the next `up`. See `postmortem-disk-2026-11-03.md`.

## Escapes

| Finding | Caught by | Should have been | Category | Countermeasure |
|---|---|---|---|---|
| Header comment called the SPA image "published" | reviewer | builder | spec gap | none: one-off |
| Reliance on the default docker driver undocumented | reviewer | builder | non-standard mechanism | comment in both workflows |
| Verifier skipped its Copilot run | supervisor | supervisor (dispatch wording) | script error | dispatch template names the Copilot run explicitly |

## Metrics

| Fix rounds | Full-suite runs | Wall time | Disk change |
|---|---|---|---|
| 1 | 1 (builder) | builder 20 min, verifier 1 min, reviewer 3 min, fix 1 min | free disk 9.6 GiB (trigger); 77 DisputeDesk dangling images removed |

## CI escape and fix round 2

- commit "refactor(infra): the gateway image takes the built SPA through a named build context, not frontend source (ad-011 change 3)" (2026-11-03) failed CI run 41709176783 (`platform`, `e2e`): `service "gateway" declares unknown service "spa" as additional contexts spa`. The runner's Compose 2.38.2 rejects a `service:` context to a service under an inactive profile; fixed upstream in v5.6.0 (docker/compose#14223, #14242, compose-spec/compose-go#931). Local and AWS run v5.6.1. Reverted by commit "Revert "refactor(infra): the gateway image takes the built SPA through a named build context, not frontend source (ad-011 change 3)"" (2026-11-03) to keep main green.
- Fix round 2 (fresh builder): `ci.yml` installs Compose v5.6.1 for the runner user in `platform` and `e2e`, pinned by the release's SHA-256 (the same pin as `infra/terraform/modules/compute/main.tf`, each pointing at the other), and fails if `docker compose version --short` differs. Reproduced in a Linux `docker:cli` container: the user plugin shadows the system 2.38.2; a wrong SHA-256 stops the step. README and onboarding now require Compose v5.6.0 or later.
- Second fix round on one slice: trigger. Postmortem: the supervisor checked `service:` with a profiled service on local Compose v5.6.1 only and wrote it into the brief as verified; CI's Compose version was never checked. Countermeasure: a brief that relies on a tool behaviour names the version every environment runs (local, CI runner, AWS) and checks the oldest.

| Finding | Caught by | Should have been | Category | Countermeasure |
|---|---|---|---|---|
| `service:` context to a profiled service unsupported on CI's Compose 2.38.2 | CI | supervisor (brief), builder | environment or tooling | check behaviour on the oldest tool version in use; CI now pins Compose |
