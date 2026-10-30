# One-shot Zitadel provisioner: bash, curl and jq on a pinned Alpine base, packages pinned to exact versions.
FROM alpine:3.24.3@sha256:be2656061b536c1883c4034790e21d0a09c5aaaf51968b89368a3c0371ea2c9a
RUN apk add --no-cache bash=5.3.9-r1 curl=8.23.0-r0 jq=1.8.2-r0
COPY provision.sh verify.sh label-policy.sh /
ENTRYPOINT ["bash", "/provision.sh"]
