#!/usr/bin/env bash
# Prints the fingerprint of a worktree's uncommitted change, the one verify-with-copilot.sh records beside the commit:
# the first 12 hex characters of the SHA-256 of `git diff <base>` followed by the SHA-256 line of every untracked,
# not ignored file (agents leave their work uncommitted). The same change gives the same fingerprint, so a verdict
# can be matched to the change it checked.
# Usage: scripts/diff-hash.sh <worktree> <base>    (verify-with-copilot.sh passes its worktree root and HEAD)
# Exit codes: 0 printed; 2 usage or git failed.
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: scripts/diff-hash.sh <worktree> <base>" >&2; exit 2; }
worktree=$1; base=$2
cd "$worktree" || exit 2
git rev-parse --verify --quiet "$base^{commit}" > /dev/null || { echo "diff-hash: $base is not a commit" >&2; exit 2; }

# Untracked paths are listed from the worktree root, so shasum prints the same relative paths wherever it is called.
cd "$(git rev-parse --show-toplevel)"
{ git diff "$base"; git ls-files -o --exclude-standard -z | xargs -0 shasum -a 256; } | shasum -a 256 | cut -c1-12
