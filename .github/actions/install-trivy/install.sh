#!/usr/bin/env bash
# Installs Trivy for Security's trivy job and CI's publish job, the one place its version and SHA-256 are pinned.
# Downloaded from its GitHub release and checked against the pinned SHA-256, the same idea as pinning actions to a
# commit SHA: a re-tagged or replaced release fails the check. (The trivy-action and setup-trivy tags were
# force-pushed with malicious code in March 2026.)
# Run by .github/actions/install-trivy/action.yml; needs RUNNER_TEMP and GITHUB_PATH (GitHub Actions sets them).
set -euo pipefail

version=0.76.1
sha256=d7cbe3137fb8a48a17574bede3dd6beb16742d1e9772929940fc90082c39d9c0

curl -fsSL --max-time 300 -o "$RUNNER_TEMP/trivy.tar.gz" \
  "https://github.com/aquasecurity/trivy/releases/download/v${version}/trivy_${version}_Linux-64bit.tar.gz"
echo "${sha256}  $RUNNER_TEMP/trivy.tar.gz" | sha256sum --check --strict
mkdir -p "$RUNNER_TEMP/bin"
tar -xzf "$RUNNER_TEMP/trivy.tar.gz" -C "$RUNNER_TEMP/bin" trivy
echo "$RUNNER_TEMP/bin" >> "$GITHUB_PATH"
