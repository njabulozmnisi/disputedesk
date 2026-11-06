#!/usr/bin/env bash
# Path check: every infra/... path named in a file of the repository exists (a Compose build context or mount, a
# workflow step, a script, a test, Terraform, a doc or a record), so a moved or deleted file leaves no stale mention.
# A path with a wildcard counts when the folder before its first wildcard exists.
# One exception: a record (docs/verification/, docs/tickets/) may name a file that an earlier commit held and that is
# gone since, because a record describes the tree of its own day.
# Searches tracked and untracked, not ignored files (git grep --untracked), so an uncommitted change is checked as it
# stands. Prints file: path for each stale mention.
# Usage: scripts/check-infra-paths.sh    Exit codes: 0 every path exists; 1 a stale path; 2 git failed.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 2

# Every path any commit has held, for the record exception.
history=$(git log --all --format= --name-only | sort -u) || exit 2

# file:infra/... for each mention: infra/ at the start of a path (after a non-path character, or after ./), never
# inside another path such as some/infra/.
mentions=$(git grep --untracked -o -I -E '(^|[^A-Za-z0-9_/.-]|\./)infra/[A-Za-z0-9_.*/\\-]*' -- . ':!scripts/check-infra-paths.sh' \
  | sed -E 's#^([^:]*):.*(infra/[A-Za-z0-9_.*/\\-]*)$#\1:\2#' | sort -u) || [ $? -eq 1 ] || exit 2

stale=0
while IFS=: read -r file path; do
  [ -n "$path" ] || continue
  path=${path//\\/}                    # a regex's escaped dot (main\.tf) names main.tf
  path=${path%%[.,-]}                  # a full stop, comma or dash that ends the sentence, not the path
  checked=${path%%\**}                 # with a wildcard, the folder before it
  [ -e "$checked" ] && continue
  case $file in
    docs/verification/* | docs/tickets/*)
      if grep -qxF -e "$path" -e "${path%/}" <<< "$history" || grep -qF -- "${path%/}/" <<< "$history"; then
        continue
      fi ;;
  esac
  echo "$file: $path"
  stale=1
done <<< "$mentions"

if [ "$stale" -eq 1 ]; then
  echo "check-infra-paths: the paths above do not exist" >&2
  exit 1
fi
echo "check-infra-paths: every infra/ path named in the repository exists"
