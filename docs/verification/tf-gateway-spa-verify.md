# tf-gateway-spa verification

Base HEAD 9836438470ddbace5318de93906e8796b1d6174d, uncommitted diff (9 files), diff hash 4522756198ef.
Read-only checks only; no Copilot run, stack or image build (per the dispatch). Verdict: PASS.

| # | Criterion | Result |
|---|---|---|
| 1 | Gateway Dockerfile | Pass. Node stage gone; `COPY --from=spa /dist/ /usr/share/nginx/html/`; COPY paths relative to `infra/nginx/`; header and usage lines updated. |
| 2 | Compose | Pass. `spa` service (`build: ./frontend`, `disputedesk-spa:local`, `profiles: [build]`); gateway `context: ./infra/nginx`, `additional_contexts: { spa: "service:spa" }`. `infra/aws/check-compose.sh` extended (spa not in the default service list, not in the AWS render, profile and named context asserted) and run: CHECK OK, rc 0. AWS override still builds nothing (its checks pass). |
| 3 | CI | Pass. ci.yml publish builds the SPA image with `BROWSER_TELEMETRY=off` and the gateway with `--build-context spa=docker-image://disputedesk-spa:publish`, SPA image never pushed; security.yml builds the same with telemetry on; Trivy gateway scan untouched. |
| 4 | Root .dockerignore | Pass. Removed; no build uses the repository root as context (all contexts are backend, frontend, infra/nginx, infra/zitadel/provision, infra/probe). `infra/nginx/` holds only the Dockerfile and templates. |
| 5 | Same output | Supervisor to confirm: needs Docker. Relies on the builder's byte-for-byte comparison. `infra/aws/gateway.test.sh` is rewritten to build SPA images then gateways via named contexts; reads correct statically, not run. |
| 6 | Every reader | Pass in the builder's paths (docker-compose.yml, infra/README.md, ci.yml, security.yml, infra/smoke.sh now uses `--profile build` so the spa image is removed on clean slate). |

Floor guard: clean. Contract drift: none (no backend or openapi.json change).

## For the supervisor
- Confirm criterion 5 and run `infra/aws/gateway.test.sh` with Docker.
- Reference outside the builder's paths: `docs/architecture/architecture.md:342` still says "the SPA built in a Node stage of the gateway image". `docs/production-path.md:351` mentions `infra/nginx/Dockerfile` (still accurate).
- Non-standard mechanisms: none; both named contexts and compose `additional_contexts` follow cited Docker docs.
