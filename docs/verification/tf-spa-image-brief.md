# The frontend builds its own SPA image (Small tier)

Slice of decision record `docs/decisions/ad-011-team-folders-in-one-repo.md` (accepted 2026-11-03), change 3, frontend half. The frontend folder publishes the built SPA files; today `infra/nginx/Dockerfile` compiles `frontend/` source. This slice adds the frontend's own build; the next slice (`tf-gateway-spa`) switches the gateway to it. Nothing consumes the new image yet, so no behaviour changes.

## Criteria

1. **`frontend/Dockerfile`.** One Node build stage moved from `infra/nginx/Dockerfile`'s `spa` stage: the same `node:24.22.1-alpine` tag and digest, `npm ci` from `package.json` and `package-lock.json` first (layer caching), then the sources, then `npm run build` with `ARG BROWSER_TELEMETRY=on` passed as `VITE_BROWSER_TELEMETRY` (the existing comment and its sources move with it). The final stage holds only the built files at `/dist/` (`FROM scratch` + `COPY --from=build`), so a consumer copies them with `COPY --from=<context> /dist/ ...` (Docker, [Multi-stage builds](https://docs.docker.com/build/building/multi-stage/)). Build context: `frontend/` itself.
2. **`frontend/.dockerignore`.** Sends nothing the build does not read: no `node_modules/`, `dist/`, `.env*`, test output or coverage.
3. **Proof.** `docker build -t disputedesk-spa:check frontend` succeeds; the image holds `/dist/index.html` and the hashed assets, and `--build-arg BROWSER_TELEMETRY=off` produces a bundle without the browser-tracing setup (the same check story 7.11 used; name it in the report).
4. **`frontend/README.md`.** The team folder's entry point, short: what the folder is, the npm scripts, that the API client is generated from `../backend/openapi.json`, that `Dockerfile` publishes the built SPA (and the build argument), and that the four login branding files in `src/theme/` (`tokens.css`, the Nunito Sans woff2, both wordmarks) are read by provisioning, so renaming them is a cross-team change. Link the root `AGENTS.md` and `docs/conventions/frontend.md`.
5. **Checks.** typecheck, lint, format, `npm test`.

## Out of scope

`infra/nginx/Dockerfile`, Compose, CI and `.dockerignore` at the root (next slice, backend lane); moving theme files (human decision 2026-11-03: they stay).
