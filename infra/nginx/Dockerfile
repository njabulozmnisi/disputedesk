# syntax=docker/dockerfile:1
# The gateway image: the SPA built in a Node stage, served by unprivileged nginx with the edge rules (AD-23).
# Build context: the repository root, so one build reads frontend/ and infra/nginx/ (the root .dockerignore sends
# nothing else).
#   docker build -f infra/nginx/Dockerfile -t disputedesk-gateway .
# Images are pinned by tag and by multi-arch index digest. The Node tag matches .nvmrc.

FROM node:24.22.1-alpine@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53 AS spa
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.31.7-alpine@sha256:b5047b9b76845eac124358373c6852377c52e4b05834a47647e955dd91cb8f1e
# The newest base still ships brotli-libs and curl/libcurl with HIGH CVEs that Alpine 3.24 has fixed, so upgrade
# just those (Trivy image scan, security.yml). apk needs root; USER 101 below drops it again.
# shortcut: unpinned package upgrade on a pinned base, drop it once a newer base tag ships the fixed packages.
USER root
RUN apk upgrade --no-cache brotli-libs curl libcurl
# The rendered templates are the only configuration: without the image's own nginx.conf, a failed render stops
# nginx from starting instead of letting it serve without the edge rules.
RUN rm /etc/nginx/nginx.conf /etc/nginx/conf.d/default.conf
# The image's entrypoint renders /etc/nginx/templates/*.template with envsubst at start. The template is the whole
# nginx.conf, so the output goes to /etc/nginx instead of the default conf.d. The filter limits substitution to the
# GATEWAY_* variables, so nginx's own $variables are never replaced by an environment variable of the same name.
ENV NGINX_ENVSUBST_OUTPUT_DIR=/etc/nginx \
    NGINX_ENVSUBST_FILTER=^GATEWAY_
COPY infra/nginx/templates/ /etc/nginx/templates/
COPY --from=spa /src/frontend/dist/ /usr/share/nginx/html/
# The base image already runs as its non-root nginx user (UID 101); restated so the Dockerfile says so (Trivy DS-0002).
USER 101
