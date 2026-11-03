# tf-spa-image review (Small tier, ad-011 change 3, frontend half)

- Builder: built; `frontend/Dockerfile` (Node stage copied from the gateway's, final `FROM scratch` stage holding `/dist/`), allowlist `frontend/.dockerignore`, `frontend/README.md`; typecheck, lint, format, 666 tests pass; the 7.11 bundle check holds (tracing on: `otlp/v1/traces` present; off: absent). Added `USER 65534`: without it Trivy DS-0002 (HIGH) fails security.yml's config scan.
- Reviewer: approve. The new image's `/dist/` is byte-identical (SHA-256, 7 files) to the gateway's current Node stage output, telemetry on and off. One low (the docker command runs from the repository root; README did not say so) and two nits (USER comment reason; allowlist note for future build inputs).
- Fix round 1 (fresh builder): the three fixes, comment and prose only; format and the image build pass; supervisor read the delta.

## Escapes

| Finding | Caught by | Should have been | Category | Countermeasure |
|---|---|---|---|---|
| README did not say where the docker command runs | reviewer | builder | spec gap | none: one-off |

## Metrics

| Fix rounds | Full-suite runs | Wall time | Disk change |
|---|---|---|---|
| 1 | frontend suite 1 (builder) | builder 4 min, reviewer 4 min, fix 1 min | worktrees removed after merge |
