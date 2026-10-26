# The Aspire dashboard image plus a static busybox installed under the applet name `wget`, so Compose can
# healthcheck it: the image is chiseled (no shell, no HTTP client). The api gets the same probe from the
# `api-local` stage in backend/Dockerfile.
FROM busybox:1.38.0-musl@sha256:ea2b9914a16a4ac1981994af97b318f7c7d4db76b580c56177f08bf76f4a0be8 AS probe

FROM mcr.microsoft.com/dotnet/aspire-dashboard:13.6.1@sha256:03099004e132a7ca2feb66c5fa24715ffeaaf2fffab6f8b2cda23de3abd3ea45
COPY --from=probe /bin/busybox /usr/local/bin/wget
# The base image already runs as its non-root app user; restated so the Dockerfile says so (Trivy DS-0002).
USER $APP_UID
