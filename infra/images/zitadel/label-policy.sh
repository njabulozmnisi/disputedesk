#!/usr/bin/env bash
# Prints Zitadel's UpdateLabelPolicy body (Admin API v1) for the hosted login pages, generated from the theme file:
#   label-policy.sh frontend/src/theme/tokens.css
# Offline: no PAT, no network, so CI runs it as it is. provision.sh sends its output; verify.sh checks against it.
# Light values come from the first `:root {` block, dark values from the `:root` inside
# `@media (prefers-color-scheme: dark)`. Each token must be declared exactly once per block, on a line whose first
# non-blank text is its name, as `<name>: #rrggbb;` (a comment may follow the `;`); anything else exits non-zero
# naming the token. This is a line lookup, not a CSS parser: a declaration that does not start its own line is not
# supported, and inside a block a comment line starts with `/*` or `*`, as the theme file writes them.
# Every other policy field keeps its default.
set -euo pipefail

TOKENS=${1:?usage: label-policy.sh <tokens.css>}

# block START: the lines after the first line containing START, up to the first line containing `}`.
block(){ awk -v start="$1" 'on && /}/ {exit} on {print} !on && index($0, start) {on=1}' "$TOKENS"; }

# colour BLOCK-TEXT BLOCK-NAME TOKEN: the token's one #rrggbb value, from the lines whose first non-blank text is
# `TOKEN:`, so `--primary:` never matches `--primary-hover:` and a comment line is never read.
colour(){
  local declarations count value
  declarations=$(grep -E -- "^[[:space:]]*$3:" <<< "$1" || true)
  count=$(grep -c . <<< "$declarations" || true)
  if [ "$count" != 1 ]; then
    echo "label-policy: $3 is declared $count times in the $2 block, not once" >&2; exit 1
  fi
  # The text between the colon and the first `;`. A line with no `;` is left whole and fails the check below.
  value=$(sed -E "s/^[[:space:]]*$3:[[:space:]]*([^;]*);.*$/\1/" <<< "$declarations")
  if ! [[ "$value" =~ ^#[0-9a-fA-F]{6}$ ]]; then
    echo "label-policy: $3 in the $2 block is '$value', not #rrggbb" >&2; exit 1
  fi
  printf '%s' "$value"
}

light=$(block ':root {')
dark=$(block '@media (prefers-color-scheme: dark)')
# One assignment each, so a rejected token stops the script (set -e) before anything is printed.
primary=$(colour "$light" light --primary)
background=$(colour "$light" light --background)
warn=$(colour "$light" light --destructive)
font=$(colour "$light" light --foreground)
primaryDark=$(colour "$dark" dark --primary)
backgroundDark=$(colour "$dark" dark --background)
warnDark=$(colour "$dark" dark --destructive)
fontDark=$(colour "$dark" dark --foreground)

# jq --arg passes each value as a JSON string, never pasted into the JSON text (jq manual).
jq -n --arg primary "$primary" --arg background "$background" --arg warn "$warn" --arg font "$font" \
  --arg primaryDark "$primaryDark" --arg backgroundDark "$backgroundDark" --arg warnDark "$warnDark" \
  --arg fontDark "$fontDark" \
  '{primaryColor: $primary, backgroundColor: $background, warnColor: $warn, fontColor: $font,
    primaryColorDark: $primaryDark, backgroundColorDark: $backgroundDark, warnColorDark: $warnDark,
    fontColorDark: $fontDark, themeMode: "THEME_MODE_AUTO"}'
