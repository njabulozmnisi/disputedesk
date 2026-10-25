#!/usr/bin/env bash
# One-time machine setup and measurement for working on DisputeDesk.
# Running the app needs only Docker; this is for development.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

echo "== Machine"
if [[ "$(uname)" == "Darwin" ]]; then
  echo "CPU: $(sysctl -n machdep.cpu.brand_string), $(sysctl -n hw.ncpu) cores"
  echo "RAM: $(( $(sysctl -n hw.memsize) / 1024 / 1024 / 1024 )) GiB"
else
  echo "CPU: $(nproc) cores"; echo "RAM: $(free -g | awk '/Mem:/ {print $2}') GiB"
fi
if docker info >/dev/null 2>&1; then
  echo "Docker: $(docker info --format '{{.NCPU}} {{.MemTotal}}' | awk '{printf "%s CPUs, %.1f GiB\n", $1, $2/1024/1024/1024}')"
else
  echo "Docker: not running"
fi

echo "== Tools"
for tool in git docker dotnet node npm uv gitleaks; do
  if command -v "$tool" >/dev/null; then printf '%-9s %s\n' "$tool" "$("$tool" --version 2>&1 | head -1)"
  else printf '%-9s MISSING\n' "$tool"; fi
done

want_node=$(cat .nvmrc)
if [[ "$(node --version 2>/dev/null)" != "v$want_node" ]]; then
  echo "WARNING: node $(node --version 2>/dev/null) is active; this repo pins v$want_node (.nvmrc). Run: nvm use"
fi

echo "== Repo hooks"
git config core.hooksPath .githooks
echo "git hooks enabled from .githooks (gitleaks and forbidden-terms checks before every commit)"
[[ -f .claude/forbidden-terms.local ]] || echo "WARNING: create .claude/forbidden-terms.local (one term per line, gitignored)"

echo "== Planning skills"
echo "For planning work, restore BMAD with: scripts/bmad-install.sh"

echo "== Study notes"
mkdir -p "$HOME/disputedesk-study"
echo "$HOME/disputedesk-study ready"
