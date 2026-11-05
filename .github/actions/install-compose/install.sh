#!/usr/bin/env bash
# Installs the Docker Compose plugin every CI job that runs docker compose needs. The runner image's preinstalled
# Compose is older and rejects the gateway's service:spa build context (fixed in v5.6.0).
# Docker, "Install the plugin manually", for the active user: the CLI searches ~/.docker/cli-plugins before the system
# plugin directories, so this plugin shadows the preinstalled one.
# The version and SHA-256 are the AWS instance's too (infra/terraform/modules/compute/main.tf, compose_version and
# compose_sha256); change them together.
# Run by .github/actions/install-compose/action.yml; needs RUNNER_TEMP (GitHub Actions sets it).
set -euo pipefail

version=5.6.1
sha256=57737f4236e6d3ccea7291995203f8f018058783b4c3a9469119811a0b5db8c0

plugins="${DOCKER_CONFIG:-$HOME/.docker}/cli-plugins"
mkdir -p "$plugins"
# A GET, so retrying is safe; curl removes a failed partial download before it retries.
curl -fsSL --max-time 300 --retry 3 --retry-all-errors -o "$RUNNER_TEMP/docker-compose" \
  "https://github.com/docker/compose/releases/download/v${version}/docker-compose-linux-x86_64"
echo "${sha256}  $RUNNER_TEMP/docker-compose" | sha256sum --check --strict
install -m 0755 "$RUNNER_TEMP/docker-compose" "$plugins/docker-compose"
docker compose version
if [ "$(docker compose version --short)" != "$version" ]; then
  echo "::error::docker compose is not v$version"
  exit 1
fi
