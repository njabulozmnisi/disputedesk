#!/usr/bin/env bash
# Named mutex for shared test resources, so parallel agents don't collide.
#   stack          the full docker compose stack (QA, frontend smoke checks)
#   testcontainers the single Testcontainers slot (integration tests)
# Usage:
#   scripts/test-lock.sh run <lock> <owner> -- <command...>   acquire, run, release
#   scripts/test-lock.sh status [<lock>]
set -euo pipefail

LOCK_ROOT="${DISPUTEDESK_LOCK_DIR:-${TMPDIR:-/tmp}/disputedesk-locks}"
STALE_SECONDS="${DISPUTEDESK_LOCK_STALE:-7200}"
WAIT_SECONDS="${DISPUTEDESK_LOCK_WAIT:-3600}"
mkdir -p "$LOCK_ROOT"

lock_dir() { echo "$LOCK_ROOT/$1.lock"; }

holder_alive() {
  local dir=$1 pid started
  pid=$(cat "$dir/pid" 2>/dev/null || echo "")
  started=$(cat "$dir/started" 2>/dev/null || echo 0)
  [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null && (( $(date +%s) - started < STALE_SECONDS ))
}

acquire() {
  local dir; dir=$(lock_dir "$1")
  local deadline=$(( $(date +%s) + WAIT_SECONDS ))
  until mkdir "$dir" 2>/dev/null; do
    if ! holder_alive "$dir"; then
      echo "test-lock: clearing stale '$1' lock held by $(cat "$dir/owner" 2>/dev/null || echo unknown)" >&2
      rm -rf "$dir"; continue
    fi
    local now; now=$(date +%s)
    (( now < deadline )) || { echo "test-lock: timed out waiting for '$1' (held by $(cat "$dir/owner"))" >&2; exit 75; }
    sleep $(( deadline - now < 5 ? deadline - now : 5 ))
  done
  echo "$2" > "$dir/owner"; echo "$$" > "$dir/pid"; date +%s > "$dir/started"
}

release() { rm -rf "$(lock_dir "$1")"; }

case "${1:-}" in
  run)
    [[ $# -ge 5 && "$4" == "--" ]] || { echo "usage: $0 run <lock> <owner> -- <command...>" >&2; exit 64; }
    name=$2 owner=$3; shift 4
    acquire "$name" "$owner"
    trap 'release "$name"' EXIT INT TERM
    "$@"
    ;;
  status)
    for dir in "$LOCK_ROOT"/${2:-*}.lock; do
      [[ -d "$dir" ]] || { echo "no locks held"; break; }
      echo "$(basename "$dir" .lock): $(cat "$dir/owner") (pid $(cat "$dir/pid"), since $(date -r "$(cat "$dir/started")" +%T))"
    done
    ;;
  *) echo "usage: $0 {run|status} ..." >&2; exit 64 ;;
esac
