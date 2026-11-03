# tf-gateway-spa fixes (one round, from the review)

Base: `commit "feat(frontend): the frontend builds its own SPA image (frontend/Dockerfile), not yet used by the gateway (ad-011 change 3)" (2026-11-03)`. Patch: `/private/tmp/claude-501/-Users-njabulomnisi-Projects-Dojo-DisputeDesk/77858989-3f5b-4d04-8184-f4d922dc16d1/scratchpad/tf-gateway-spa.patch` (apply with `git apply --binary --exclude='docs/*' <patch>`).

1. **Nit, criterion 3.** `infra/images/gateway/Dockerfile` line 3 says the SPA comes from "the frontend's published image", but the SPA image is never published. Say "from the SPA image (`frontend/Dockerfile`, its files at `/dist/`; ad-011)".
2. **Nit.** `.github/workflows/ci.yml` publish step (about lines 396-400), and the same build in `security.yml`: add one comment line saying `docker-image://` of a local, never-pushed tag works because the default `docker` driver reads the daemon's image store; a `docker-container` builder (for example from `setup-buildx-action`) would try a registry pull instead.
