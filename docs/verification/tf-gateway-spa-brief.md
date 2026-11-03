# The gateway image takes the built SPA, not frontend source (Small tier)

Slice of decision record `docs/decisions/ad-011-team-folders-in-one-repo.md` (accepted 2026-11-03), change 3, infra half. Needs `tf-spa-image` merged (`frontend/Dockerfile`, final stage holds `/dist/`). The served SPA and the gateway's behaviour do not change.

## Criteria

1. **Gateway Dockerfile.** `infra/images/gateway/Dockerfile` loses its Node stage and copies the SPA with `COPY --from=spa /dist/ /usr/share/nginx/html/`, where `spa` is a named build context (Docker, [Named contexts](https://docs.docker.com/build/building/context/#named-contexts)). Its build context becomes `infra/images/gateway/`; its `COPY` paths, header comment and usage lines follow.
2. **Compose.** `docker-compose.yml` gains a build-only `spa` service (`build: ./frontend`, image `disputedesk-spa:local`) under a profile (for example `build`), and the gateway's build sets `additional_contexts: { spa: "service:spa" }` (Compose, [additional_contexts](https://docs.docker.com/reference/compose-file/build/#additional_contexts)). `docker compose up --build --wait` builds the SPA for the gateway and never creates an `spa` container (verified by the supervisor with Compose v5.6.1, 2026-11-03); extend an existing check (`infra/deploy/aws/check-compose.sh` or a platform test) so a render that would start `spa` without the profile fails. The AWS override still builds nothing.
3. **CI.** `ci.yml`'s publish job builds the SPA image with `--build-arg BROWSER_TELEMETRY=off` and the gateway with `--build-context spa=docker-image://<that image>`; `security.yml`'s image build does the same (telemetry on, as today). Trivy still scans the gateway image; the SPA image is not published (only its files ship, inside the gateway).
4. **Root `.dockerignore`.** Removed if no build uses the repository root as its context any more; otherwise reduced to what remains. Each build context sends only what it reads.
5. **Same output.** The SPA files served by the new gateway image equal those of the old image built from `main` (compare the file list and hashes under `/usr/share/nginx/html/`, telemetry on and off). `infra/deploy/aws/gateway.test.sh` and the gateway-related platform tests pass.
6. **Every reader.** Comments and docs in your paths that describe the gateway compiling the SPA (`docker-compose.yml`, `infra/README.md`, `ci.yml`, `security.yml`) say it now takes the built files. List references outside your paths for the supervisor.

## Out of scope

The frontend Dockerfile itself, any nginx template change, publishing an SPA image to GHCR.
