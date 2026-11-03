# tf-fixtures fixes (one round, from the review)

Base: `093211d8425027c3dbd78f88e9ccb2ad5946c6d6`. Patch: `/private/tmp/claude-501/-Users-njabulomnisi-Projects-Dojo-DisputeDesk/77858989-3f5b-4d04-8184-f4d922dc16d1/scratchpad/tf-fixtures.patch` (apply with `git apply --binary --exclude='docs/*' <patch>`; it holds the builder's change and QA's e2e path edits).

1. **Low, criterion 4.** `backend/README.md` line 17 says these are the commands CI's `backend` job runs, but that job also runs the OpenAPI drift check (`git diff --exit-code -- openapi.json`) and the EF model drift check (`dotnet ef migrations has-pending-model-changes ...`). List both, copied from `.github/workflows/ci.yml`.
2. **Nit.** `backend/README.md` "What it reads from the root" (about line 34): add one line saying the tests also read `docker-compose.yml` and `scripts/`, and that `ApiReferenceTests` compares the API's Scalar headers with `infra/nginx/templates/scalar-headers.conf.template` (a cross-team consistency check; the provision and gateway-template cases in `PlatformScriptTests` move to `infra/tests/` in a later slice).

Not fixed: `FixturesFile.FindPath()` no longer searches (nit, renaming touches about 10 callers).
