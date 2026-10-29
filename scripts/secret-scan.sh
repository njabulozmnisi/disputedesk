#!/usr/bin/env bash
# Secret scan of the uncommitted change: every tracked change against HEAD plus every untracked file,
# the same content the pre-commit hook (`gitleaks git --staged`) will see once it is staged. Run it from
# the worktree before reporting a story built, and in the security stage. Exit 0 clean, 1 leaks found.
# Usage: scripts/secret-scan.sh
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
command -v gitleaks >/dev/null || { echo "secret-scan: gitleaks not installed (brew install gitleaks)" >&2; exit 2; }

{
  git diff HEAD
  # Untracked files as additions; `diff` exits 1 when files differ, which is expected here.
  git ls-files --others --exclude-standard -z | while IFS= read -r -d '' file; do
    diff -u /dev/null "$file" || true
  done
} | gitleaks stdin --config .gitleaks.toml --redact --no-banner
