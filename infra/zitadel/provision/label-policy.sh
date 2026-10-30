#!/usr/bin/env bash
# Prints Zitadel's UpdateLabelPolicy body (Admin API v1) for the hosted login pages, generated from the theme file:
#   label-policy.sh frontend/src/theme/tokens.css
# Offline: no PAT, no network, so CI runs it as it is. provision.sh sends its output; verify.sh checks against it.
# Light values come from the first `:root` block, dark values from the `:root` inside
# `@media (prefers-color-scheme: dark)`. Each token must be declared exactly once per block as #rrggbb;
# anything else exits non-zero naming the token. Every other policy field keeps its default.
set -euo pipefail

TOKENS=${1:?usage: label-policy.sh <tokens.css>}

# The theme file without its comments, so a commented-out declaration is never read. As in CSS Syntax Level 3
# (section 4.3.2, consume comments), a comment runs from /* to the next */, across lines, or to the end of the file.
css=$(awk '{ text = text $0 "\n" } END {
  while ((open = index(text, "/*")) > 0) {
    rest = substr(text, open + 2)
    end = index(rest, "*/")
    if (end == 0) { text = substr(text, 1, open - 1); break }
    text = substr(text, 1, open - 1) substr(rest, end + 2)
  }
  printf "%s", text
}' "$TOKENS")

# block START: the lines after the first line containing START, up to the first line containing `}`.
block(){ awk -v start="$1" 'on && /}/ {exit} on {print} !on && index($0, start) {on=1}' <<< "$css"; }

# colour BLOCK-TEXT BLOCK-NAME TOKEN: the token's one #rrggbb value. `--primary:` never matches `--primary-hover:`.
# CSS Syntax Level 3 (consume a declaration) allows whitespace, newlines included, between the name and the colon, so
# the block's lines are joined into one before the search.
colour(){
  local declarations count value
  declarations=$(tr '\n' ' ' <<< "$1" | grep -oE -- "(^|[[:space:];{])$3[[:space:]]*:[^;]*" || true)
  count=$(grep -c . <<< "$declarations" || true)
  if [ "$count" != 1 ]; then
    echo "label-policy: $3 is declared $count times in the $2 block, not once" >&2; exit 1
  fi
  # The name is cut from the start of the match only, so a value that repeats the name stays in the value.
  value=$(sed -E "s/^[[:space:];{]?$3[[:space:]]*:[[:space:]]*//; s/[[:space:]]+$//" <<< "$declarations")
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
