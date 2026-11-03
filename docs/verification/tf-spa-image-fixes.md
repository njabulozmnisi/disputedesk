# tf-spa-image fixes (one round, from the review)

Base: `8dc541d44cfa6309d6b0c1fc72b8bc858b481ebd`. Patch: `/private/tmp/claude-501/-Users-njabulomnisi-Projects-Dojo-DisputeDesk/77858989-3f5b-4d04-8184-f4d922dc16d1/scratchpad/tf-spa-image.patch` (apply with `git apply --binary --exclude='docs/*' <patch>`).

1. **Low, criterion 4.** `frontend/README.md` (docker block, about line 29) and `frontend/Dockerfile` line 5: the npm commands run from `frontend/`, but `docker build -t disputedesk-spa frontend` runs only from the repository root. Say "From the repository root:" before the docker command in both places.
2. **Nit.** `frontend/Dockerfile` line 21: reword the USER comment to give the real reason: "Nothing runs in this image; a non-root USER is still required by Trivy DS-0002, which fails security.yml's config scan."
3. **Nit, criterion 2.** `frontend/.dockerignore`: add one comment line saying a new build input (for example `public/`) is added to the allowlist here, since the allowlist drops it silently otherwise.
