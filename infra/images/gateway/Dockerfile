# syntax=docker/dockerfile:1
# The gateway image: the built SPA files, served by unprivileged nginx with the edge rules (AD-23).
# The SPA comes from the SPA image (frontend/Dockerfile, its files at /dist/; ad-011) through the
# named build context spa (Docker, "Named contexts"); Compose passes it with additional_contexts. Build context: this
# folder. From the repository root:
#   docker build -t disputedesk-spa frontend
#   docker build --build-context spa=docker-image://disputedesk-spa -t disputedesk-gateway infra/images/gateway
# The browser telemetry switch is the SPA image's BROWSER_TELEMETRY build argument (story 7.11).
# The image is pinned by tag and by multi-arch index digest.

FROM nginxinc/nginx-unprivileged:1.31.7-alpine@sha256:b5047b9b76845eac124358373c6852377c52e4b05834a47647e955dd91cb8f1e
# The newest base still ships brotli-libs, curl/libcurl, libexpat, musl/musl-utils and zlib with HIGH CVEs that
# Alpine 3.24 has fixed, so upgrade just those (Trivy image scan, security.yml). apk needs root; USER 101 below drops
# it again.
# shortcut: unpinned package upgrade on a pinned base, drop it once a newer base tag ships the fixed packages.
USER root
RUN apk upgrade --no-cache brotli-libs curl libcurl libexpat musl musl-utils zlib
# The rendered templates are the only configuration: without the image's own nginx.conf, a failed render stops
# nginx from starting instead of letting it serve without the edge rules.
RUN rm /etc/nginx/nginx.conf /etc/nginx/conf.d/default.conf
# The image's entrypoint renders /etc/nginx/templates/*.template with envsubst at start. The template is the whole
# nginx.conf, so the output goes to /etc/nginx instead of the default conf.d. The filter limits substitution to the
# GATEWAY_* variables, so nginx's own $variables are never replaced by an environment variable of the same name.
ENV NGINX_ENVSUBST_OUTPUT_DIR=/etc/nginx \
    NGINX_ENVSUBST_FILTER=^GATEWAY_
COPY templates/ /etc/nginx/templates/
# The AWS edge (infra/deploy/aws/compose.yml selects it with NGINX_ENVSUBST_TEMPLATE_DIR), with the app's security and
# Scalar headers and the identity origin's headers copied from the one source above.
COPY templates-aws/ /etc/nginx/templates-aws/
COPY templates/security-headers.conf.template templates/scalar-headers.conf.template \
     templates/identity-headers.conf.template /etc/nginx/templates-aws/
COPY --from=spa /dist/ /usr/share/nginx/html/
# The base image already runs as its non-root nginx user (UID 101); restated so the Dockerfile says so (Trivy DS-0002).
USER 101
