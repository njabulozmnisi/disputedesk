#!/usr/bin/env bash
# The Security workflow's filesystem and config scan (.github/workflows/security.yml, "Filesystem and config scan"),
# run locally so a Terraform, Dockerfile or Compose finding shows before merge. Same command and ignore file;
# agent worktrees and node_modules are skipped (CI's checkout has neither). Image scans stay in CI.
# Usage: scripts/security-local.sh
# Exit codes: 0 clean; 1 a HIGH or CRITICAL finding; 2 trivy not installed.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
command -v trivy >/dev/null || { echo "security-local: trivy not installed (brew install trivy; CI pins the version in security.yml)" >&2; exit 2; }

trivy fs --scanners vuln,misconfig --severity HIGH,CRITICAL --exit-code 1 --ignorefile .github/trivyignore.yaml \
  --skip-dirs .claude/worktrees --skip-dirs '**/node_modules' .
