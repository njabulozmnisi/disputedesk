# Zitadel with its provisioner: the static /app/zitadel binary, byte-identical to the pinned upstream image's, on a
# pinned Alpine with the provisioner's tools (bash, curl and jq, pinned to exact versions); Docker, "Multi-stage
# builds".
# The entrypoint and the non-root user are upstream's: /app/zitadel as zitadel (uid and gid 1000).
# The volume mount points are owned by zitadel, so a fresh named volume mounted there is too (Docker, "Volumes",
# populate a volume using a container) and nothing in the container runs as root. /provision/state holds the
# provisioned marker in the container's own filesystem, so a recreated container starts without it. The scripts call
# the binary as `zitadel`, on PATH.
FROM ghcr.io/zitadel/zitadel:v4.20.0@sha256:9ea0e9ecfdc43115da871242e40a0320f2ece864c7cd8b5d41a863a7f4902499 AS zitadel

FROM alpine:3.24.3@sha256:be2656061b536c1883c4034790e21d0a09c5aaaf51968b89368a3c0371ea2c9a
RUN apk add --no-cache bash=5.3.9-r1 curl=8.23.0-r0 jq=1.8.2-r0 \
 && addgroup -g 1000 zitadel \
 && adduser -D -H -h / -s /sbin/nologin -G zitadel -u 1000 zitadel \
 && mkdir -p /zitadel/bootstrap /provision/out /provision/state \
 && chown zitadel:zitadel /zitadel/bootstrap /provision/out /provision/state \
 && ln -s /app/zitadel /usr/local/bin/zitadel
COPY --from=zitadel /app/zitadel /app/zitadel
COPY provision.sh verify.sh label-policy.sh /provision/
USER zitadel
ENTRYPOINT ["/app/zitadel"]
