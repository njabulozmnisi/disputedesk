# Postmortem: free disk under 15 GB (2026-11-03)

**Trigger.** The tf-gateway-spa builder reported 9.6 GiB free on the build machine, under the 15 GB trigger (AGENTS.md, Continuous improvement).

**Cause.** The Docker Desktop VM disk holds 28 GB. Its BuildKit build cache is 16.2 GB (10.9 GB reclaimable). Image rebuilds across this epic's slices (gateway, SPA, api) leave dangling images and cache entries behind. The daemon is shared with other projects, so the build cache cannot be pruned per project.

**Done.**
- Removed the slice test tags (`disputedesk-spa:aws-test`, `:check`, `:local-test`; `disputedesk-gateway:aws-test`, `:local-test`).
- Removed 77 dangling images labelled with a DisputeDesk Compose project (`disputedesk`, `disputedesk-711`, `dd714`).
- Images went from 11.6 GB to 6.6 GB inside the VM. The host's free space does not move until Docker Desktop reclaims the VM disk.

**Not done (shared daemon).** `docker builder prune` and the 40 unlabelled dangling `nginx-unprivileged`-based images. Both may belong to other projects.

**Proposal.**
1. A human decision: run `docker builder prune --filter until=72h` once (it touches every project's cache), or set a build cache limit in Docker Desktop (Settings, Docker Engine, `builder.gc`; Docker, [Build garbage collection](https://docs.docker.com/build/cache/garbage-collection/)).
2. Builders tag throwaway comparison images with a DisputeDesk label or prefix and remove them before reporting. This is a build-story pitfall line, so it needs approval (skill change).

**Related.** `infra/tests/smoke.sh` runs `docker compose down -v`, so a builder running it wipes the shared local stack's volumes. The data is synthetic and reseeds on the next `up`, so nothing was lost that matters, but a builder should say so before running it. This is a candidate pitfall line for the same batch.

## Decisions (human, 2026-11-04)

- `docker builder prune --filter until=72h` run once: 5.0 GB reclaimed; host free 14 GiB.
- Approved: the four build-story pitfall lines (suite results file, throwaway images, `smoke.sh` wipes volumes, oldest tool version) and the removal of the dead guard and agent-file paths (`fixtures/`, `docker-compose.aws.yml`, root `.dockerignore`).
