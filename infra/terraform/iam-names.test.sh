#!/usr/bin/env bash
# Every IAM action and condition key in the Terraform policies exists in AWS's programmatic service reference (story
# 7.16; AWS Service Authorization Reference, "Simplified AWS service information for programmatic access"): public JSON
# read over HTTPS with no credentials, so no AWS API is called. Names are read from every *.tf under infra/terraform by
# line rules, and a policy written in any shape the rules cannot read fails the check, so no name is skipped silently.
# The last section proves the check fails: three copies of the files, each with one name misspelt, must be refused.
#   infra/terraform/iam-names.test.sh
# Needs bash, curl and jq. Only names are checked, not what a policy grants.
#
# Known limits, none of which the tree holds today: shapes the line rules pass without reading their names, and one
# that fails closed.
# - A condition key written without its colon, or an expression key such as (local.key), is not read as a key.
# - Condition = { for ... } starts with { and so is read as a map, though its keys are computed.
# - policy = jsonencode(jsondecode(file(...))) starts with jsonencode( and so is read, though its names are in the file.
# - s3's reference writes some keys with <key> placeholders (s3:ExistingObjectTag/<key>), which are not turned into
#   patterns as ${...} is, so a valid s3:ExistingObjectTag/env would be refused (fails closed).
set -euo pipefail
cd "$(dirname "$0")/../.."
# +(...) and @(...) are extglob patterns, parsed only when extglob is on (Bash Reference Manual, "Pattern Matching").
shopt -s extglob

section(){ printf '\n== %s\n' "$*"; }
fail(){ echo "IAM NAMES TEST FAIL: $*" >&2; exit 1; }
ok(){ echo "ok  $*"; }
# The check's own failures: printed, never exiting, so a copy's failure can be captured and inspected.
problem(){ echo "FAIL $*" >&2; }

REFERENCE=https://servicereference.us-east-1.amazonaws.com
INDEX_URL=$REFERENCE/
GLOBAL_KEYS_URL=$REFERENCE/v1/global-condition-keys.json

# The GitHub OIDC provider's two keys: documented in the IAM User Guide ("IAM and AWS STS condition context keys",
# #condition-keys-wif) but not listed in the service reference, so accepted as they are and printed as not verified.
OIDC_KEYS=(token.actions.githubusercontent.com:aud token.actions.githubusercontent.com:sub)

# The line rules, case-sensitive, so the SSM document's lower-case `action` step is not read as a policy's Action.
ACTION_LINE='^[[:space:]]*(Action|NotAction|"Action"|"NotAction")[[:space:]]*[=:](.*)$'
QUOTED='"[^"]*"'
# One double-quoted literal, or a one-line [...] list of them, then at most a trailing comma.
QUOTED_LIST="[[][[:space:]]*$QUOTED([[:space:]]*,[[:space:]]*$QUOTED)*[[:space:]]*,?[[:space:]]*[]]"
ACTION_VALUE="^[[:space:]]*($QUOTED|$QUOTED_LIST)[[:space:]]*,?[[:space:]]*\$"
ACTION_NAME='^[a-z0-9-]+:[A-Za-z0-9*?]+$'
KEY_NAME='^[A-Za-z0-9._/-]+(:[A-Za-z0-9._/-]+)+$'
POLICY_LINE='^[[:space:]]*(policy|assume_role_policy|policy_document)[[:space:]]*=(.*)$'
POLICY_VALUE='^[[:space:]]*jsonencode[(]'
CONDITION_LINE='^[[:space:]]*(Condition|"Condition")[[:space:]]*[=:](.*)$'
CONDITION_VALUE='^[[:space:]]*[{]'
# An Action, NotAction or Condition, or a policy attribute, after a character that cannot be part of an HCL identifier.
# The rules above read these names only as the first token on a line, so one found anywhere else is refused.
ELEMENT_INSIDE='(^|[^A-Za-z0-9_-])"?(Action|NotAction|Condition)"?[[:space:]]*[=:]'
POLICY_INSIDE='(^|[^A-Za-z0-9_-])(policy|assume_role_policy|policy_document)[[:space:]]*='
# A jq filter: each ${...} placeholder in a reference key name becomes the bash pattern +([!:]).
PLACEHOLDER_AS_PATTERN='gsub("\\$\\{[^}]*\\}"; "+([!:])")'
# jq filters for a reference file's shape: an array of objects with a string Name; and, for condition keys, which
# become bash patterns, no Name holding a pattern character once its ${...} segments are removed.
NAMED_ARRAY='type == "array" and all(.[]; type == "object" and (.Name | type) == "string")'
NO_PATTERN_CHARACTER='all(.[]; .Name | gsub("\\$\\{[^}]*\\}"; "") | test("[\\[\\]*?|()!@+\\\\]") | not)'
# The first double-quoted string on a line, and the rest of the line after it.
FIRST_STRING='"([^"]*)"(.*)$'
MAP_KEY_FOLLOWS='^[[:space:]]*[=:]'

tf_files(){ # directory
  find "$1" -name .terraform -prune -o -type f -name '*.tf' -print
}

# Prints "kind<TAB>name<TAB>file" once per distinct action ("action") or condition key ("key") and file under $1.
# Names every line the rules cannot read with its file and line, and then returns 1, as it does when $1 is missing or
# holds no action or no condition key.
read_names(){ # terraform-dir
  local dir=$1 files file number line unread value rest string names="" actions=0 keys=0 unreadable=0
  if [ ! -d "$dir" ]; then
    problem "$dir is missing"
    return 1
  fi
  if ! files=$(tf_files "$dir"); then
    problem "could not list the *.tf files under $dir"
    return 1
  fi
  if [ -z "$files" ]; then
    problem "no *.tf file under $dir"
    return 1
  fi

  while IFS= read -r file; do
    number=0
    while IFS= read -r line || [ -n "$line" ]; do
      number=$((number + 1))

      if [[ $line == *aws_iam_policy_document* ]]; then
        problem "$file:$number: an aws_iam_policy_document, which the line rules do not read"
        unreadable=1
      fi
      if [[ $line =~ $POLICY_LINE ]] && ! [[ ${BASH_REMATCH[2]} =~ $POLICY_VALUE ]]; then
        problem "$file:$number: a policy that is not written as jsonencode(...)"
        unreadable=1
      fi
      if [[ $line =~ $CONDITION_LINE ]] && ! [[ ${BASH_REMATCH[2]} =~ $CONDITION_VALUE ]]; then
        problem "$file:$number: a Condition whose value is not a map"
        unreadable=1
      fi

      # The part of the line no rule reads: after the first token when a rule reads that token, else the whole line.
      unread=$line
      if [[ $line =~ $ACTION_LINE ]] || [[ $line =~ $CONDITION_LINE ]] || [[ $line =~ $POLICY_LINE ]]; then
        unread=${BASH_REMATCH[2]}
      fi
      if [[ $unread =~ $ELEMENT_INSIDE ]]; then
        problem "$file:$number: an Action, NotAction or Condition that is not the first token on its line"
        unreadable=1
      fi
      if [[ $unread =~ $POLICY_INSIDE ]]; then
        problem "$file:$number: a policy attribute that is not the first token on its line"
        unreadable=1
      fi

      if [[ $line =~ $ACTION_LINE ]]; then
        value=${BASH_REMATCH[2]}
        if [[ $value =~ $ACTION_VALUE ]]; then
          rest=$value
          while [[ $rest =~ $FIRST_STRING ]]; do
            string=${BASH_REMATCH[1]}
            rest=${BASH_REMATCH[2]}
            if [[ $string =~ $ACTION_NAME ]]; then
              names+="action"$'\t'"$string"$'\t'"$file"$'\n'
              actions=$((actions + 1))
            else
              problem "$file:$number: the action \"$string\" is not <service>:<name>"
              unreadable=1
            fi
          done
        else
          problem "$file:$number: an Action that is not one double-quoted literal or a one-line list of them"
          unreadable=1
        fi
      fi

      # Every double-quoted string holding a colon that is a map key is a condition key.
      rest=$line
      while [[ $rest =~ $FIRST_STRING ]]; do
        string=${BASH_REMATCH[1]}
        rest=${BASH_REMATCH[2]}
        if [[ $string == *:* && $rest =~ $MAP_KEY_FOLLOWS ]]; then
          if [[ $string =~ $KEY_NAME ]]; then
            names+="key"$'\t'"$string"$'\t'"$file"$'\n'
            keys=$((keys + 1))
          else
            problem "$file:$number: the condition key \"$string\" holds a character outside [A-Za-z0-9._/-]"
            unreadable=1
          fi
        fi
      done
    done < "$file"
  done <<< "$files"

  if [ "$unreadable" -ne 0 ]; then
    return 1
  fi
  if [ "$actions" -eq 0 ]; then
    problem "no IAM action found under $dir"
    return 1
  fi
  if [ "$keys" -eq 0 ]; then
    problem "no condition key found under $dir"
    return 1
  fi
  printf '%s' "$names" | LC_ALL=C sort -u
}

# IAM action names and condition keys are case-insensitive (AWS IAM, "IAM JSON policy elements: Action" and
# "Condition"), so the match is bash's [[ == ]] under nocasematch, turned on only here so the line rules stay
# case-sensitive. The pattern must stay unquoted to be matched as a pattern: an unquoted right-hand side trips
# ShellCheck SC2053, and quoting it would compare it literally, so it goes unquoted inside @(...) (extglob: one of the
# patterns). @(...) also makes | and ) live, so the shape checks refuse any reference key name holding a pattern
# character.
matches(){ # text pattern
  local result=1
  shopt -s nocasematch
  if [[ $1 == @($2) ]]; then
    result=0
  fi
  shopt -u nocasematch
  return "$result"
}

# An action passes when its service is in the index and its name, where * is any characters and ? one character,
# matches one of the service's Actions[].Name.
check_action(){ # action file reference-dir
  local action=$1 file=$2 reference=$3 service=${1%%:*} pattern=${1#*:} names name
  if ! jq -e --arg service "$service" 'any(.[]; .service == $service)' "$reference/index.json" > /dev/null; then
    problem "action $action in $file: the service reference has no service $service"
    return 1
  fi
  if ! names=$(jq -r '.Actions[].Name' "$reference/services/$service.json"); then
    problem "action $action in $file: could not read the service reference for $service"
    return 1
  fi
  while IFS= read -r name; do
    if matches "$name" "$pattern"; then
      ok "action $action  ($file)"
      return 0
    fi
  done <<< "$names"
  problem "action $action in $file: no such action in the service reference for $service"
  return 1
}

# A key passes when it matches one of the reference's key patterns, or is one of the two OIDC keys, which contain no
# pattern character, so matching one is an exact, case-insensitive comparison.
check_key(){ # key file key-patterns
  local key=$1 file=$2 patterns=$3 pattern oidc_key
  while IFS= read -r pattern; do
    if matches "$key" "$pattern"; then
      ok "condition key $key  ($file)"
      return 0
    fi
  done <<< "$patterns"
  for oidc_key in "${OIDC_KEYS[@]}"; do
    if matches "$key" "$oidc_key"; then
      ok "condition key $key  ($file)  not verified against the service reference: an OIDC federation key in the IAM User Guide"
      return 0
    fi
  done
  problem "condition key $key in $file: in neither the global condition keys nor a fetched service's keys"
  return 1
}

# The check: every name read under $1 against the reference files in $2, one line per name and file. Returns 1, never
# exiting, when a policy cannot be read or a name is unknown.
check_names(){ # terraform-dir reference-dir
  local dir=$1 reference=$2 records patterns service_file kind name file failed=0
  records=$(read_names "$dir") || return 1

  # One lookup set: the global keys' and every fetched service's ConditionKeys[].Name, each ${...} placeholder (such
  # as ${TagKey}) turned into +([!:]), any non-empty text without a colon.
  if ! patterns=$(jq -r ".[].Name | $PLACEHOLDER_AS_PATTERN" "$reference/global-condition-keys.json"); then
    problem "could not read $reference/global-condition-keys.json"
    return 1
  fi
  for service_file in "$reference"/services/*.json; do
    # When no service was fetched (every action names an unknown service), the glob stays as written.
    if [ ! -e "$service_file" ]; then
      continue
    fi
    if ! patterns+=$'\n'$(jq -r ".ConditionKeys[].Name | $PLACEHOLDER_AS_PATTERN" "$service_file"); then
      problem "could not read $service_file"
      return 1
    fi
  done

  while IFS=$'\t' read -r kind name file; do
    if [ "$kind" = action ]; then
      check_action "$name" "$file" "$reference" || failed=1
    else
      check_key "$name" "$file" "$patterns" || failed=1
    fi
  done <<< "$records"
  return "$failed"
}

# One bounded download with no credential: -q first, so no ~/.curlrc is read; HTTPS only; no redirect followed; 10 s
# to connect, 30 s an attempt, and two retries started within 60 s (curl man page).
fetch(){ # url file
  curl -q --proto '=https' -sS --fail --connect-timeout 10 --max-time 30 --retry 2 --retry-max-time 60 -o "$2" "$1" \
    || fail "could not fetch $1"
  ok "fetched $1"
}

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
reference=$work/reference
mkdir -p "$reference/services"

section "AWS's service reference, each file fetched once"
records=$(read_names infra/terraform) || fail "infra/terraform cannot be read by the line rules (above)"
services=$(printf '%s\n' "$records" | awk -F '\t' '$1 == "action" { sub(/:.*/, "", $2); print $2 }' | LC_ALL=C sort -u) \
  || fail "could not list the services the actions name"

fetch "$INDEX_URL" "$reference/index.json"
jq -e 'type == "array" and all(.[]; type == "object" and (.service | type) == "string")' "$reference/index.json" \
  > /dev/null || fail "$INDEX_URL is not an array of objects with a string service"

fetch "$GLOBAL_KEYS_URL" "$reference/global-condition-keys.json"
jq -e "$NAMED_ARRAY and $NO_PATTERN_CHARACTER" "$reference/global-condition-keys.json" > /dev/null \
  || fail "$GLOBAL_KEYS_URL is not an array of objects with a string Name free of pattern characters"

# Each service's file is built from the documented layout, never taken from the index. A service missing from the
# index has no file to fetch: the check below fails, naming the action and its file.
for service in $services; do
  if ! jq -e --arg service "$service" 'any(.[]; .service == $service)' "$reference/index.json" > /dev/null; then
    continue
  fi
  url=$REFERENCE/v1/$service/$service.json
  fetch "$url" "$reference/services/$service.json"
  jq -e "(.Actions | $NAMED_ARRAY) and (.ConditionKeys | $NAMED_ARRAY and $NO_PATTERN_CHARACTER)" \
    "$reference/services/$service.json" > /dev/null \
    || fail "$url has no Actions and ConditionKeys arrays of objects with a string Name, the keys free of pattern characters"
done

section "every name in infra/terraform"
check_names infra/terraform "$reference" || fail "a policy cannot be read, or a name is not in the service reference (above)"

# Each copy holds the scanned files with one name misspelt, and must be refused naming exactly that name. The check's
# output is captured, and the reference already fetched is reused.
expect_refused(){ # name misspelling
  local name=$1 misspelling=$2 copy=$work/copy output=$work/copy.out files file failures
  rm -rf "$copy"
  files=$(tf_files infra/terraform)
  while IFS= read -r file; do
    mkdir -p "$copy/$(dirname "$file")"
    # The names hold no sed special character but '.', which also matches itself.
    sed "s/$name/$misspelling/g" "$file" > "$copy/$file"
  done <<< "$files"
  grep -rqF -- "$misspelling" "$copy" || fail "infra/terraform holds no $name to misspell"

  if check_names "$copy/infra/terraform" "$reference" > "$output" 2>&1; then
    fail "the check passed a copy with $name misspelt as $misspelling"
  fi
  failures=$(grep '^FAIL' "$output") || fail "the check refused the copy with $misspelling naming nothing: $(cat "$output")"
  if [ "$(grep -cvF -- "$misspelling" <<< "$failures")" != 0 ]; then
    fail "the check refused the copy with $misspelling for another reason too: $failures"
  fi
  ok "a copy with $name misspelt as $misspelling is refused, naming it"
}

section "the check fails on a misspelt name"
expect_refused ssm:SendCommand ssm:SendComand
expect_refused kms:ViaService kms:ViaServices
expect_refused token.actions.githubusercontent.com:sub token.actions.githubusercontent.cmo:sub

echo
echo "Only names were checked: not whether a policy is sufficient or least-privilege, how IAM evaluates it, whether a" \
  "condition key applies to the action beside it, how much a wildcard action grants, resource ARNs, principals or the" \
  "attached managed policies."
