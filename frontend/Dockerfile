# syntax=docker/dockerfile:1
# The SPA build: the frontend team's published artefact is the built files at /dist/ (ad-011). A consumer copies them
# with COPY --from=<named context> /dist/ ... (Docker, "Multi-stage builds" and "Named contexts").
# Build context: this folder. From the repository root:
#   docker build -t disputedesk-spa frontend
# The Node image is pinned by tag and by multi-arch index digest. The tag matches .nvmrc.

FROM node:24.22.1-alpine@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53 AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . ./
# Browser tracing is on unless the build says off (story 7.11): CI's publish job builds the image for AWS with
# --build-arg BROWSER_TELEMETRY=off, because the AWS gateway takes in no browser telemetry (AD-29). Vite reads
# VITE_BROWSER_TELEMETRY at build time (Docker, "Build variables"; Vite, "Env Variables and Modes").
ARG BROWSER_TELEMETRY=on
RUN VITE_BROWSER_TELEMETRY=$BROWSER_TELEMETRY npm run build

FROM scratch
COPY --from=build /src/dist/ /dist/
# Nothing runs in this image; a non-root USER is still required by Trivy DS-0002, which fails security.yml's config scan.
USER 65534
