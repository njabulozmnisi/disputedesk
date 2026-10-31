#!/usr/bin/env bash
# Runs the whole regression suite in its documented order, every group on its own fresh stack.
#   scripts/test-lock.sh run stack <owner> -- e2e/run-regression.sh
# Customer D can link once per database and takes three OTP sends an hour (story 6.5), and the linking limits differ between
# specs, so the specs that use D or the limits cannot share a stack (6.3 QA C1 and C3, span-privacy-check.md result 2). QA only
# reads the database (ad-005), so nothing resets D between tests; instead each group is planned to send D at most three OTPs
# (the app's hourly cap, story 6.5, stops a fourth; a group that plans over three fails in its own tests first). After each
# group the D check confirms the app's cap held and records the count. Each group is a Playwright project (https://playwright.dev/docs/test-projects) run with
# --project; the stack is reset (volumes removed) between groups.
#
#   group                 project               linking limits       D sends  specs
#   1 linking-rate-limits linking-rate-limits   production (5, 20)   3        linking-rate-limits
#   2 linking-screens     linking-screens       production (5, 20)   2        linking-screens, but the full-queue journey
#   3 linking-queue       linking-queue         production (5, 20)   3        linking-screens' full-queue journey alone
#   4 linking-verify-a    linking-verify-a      raised (1000)        2        linking-verify @verify-a: bounds, lock, blank, released
#   5 linking-verify-b    linking-verify-b      raised (1000)        2        linking-verify @verify-b: supersede, expiry
#   6 linking-verify-c    linking-verify-c      raised (1000)        2        linking-verify @verify-c: the race for D
#   7 regression          regression            raised (1000)        3        every other spec, linking-start among them
#
# Every stack gets the small queue (capacity 2). The linking job budget stays at its 10-second default; the regression group
# tells linking-start's budget test that value (E2E_LINKING_BUDGET_SECONDS). Set DISPUTEDESK_OTLP_HTTP_PORT and
# DISPUTEDESK_OTLP_GRPC_PORT when 4318 or 4317 is taken. `npm run journeys` runs on its own fresh stack (story 7.1). The stack
# is left stopped.
set -euo pipefail
cd "$(dirname "$0")/.."

export E2E_LINKING_QUEUE_CAPACITY=2
small_queue=(-f docker-compose.yml -f e2e/small-queue.compose.yml)
raised_limits=(-f docker-compose.yml -f e2e/small-queue.compose.yml -f e2e/raised-limits.compose.yml)
customer_d=$(node -p "JSON.parse(require('fs').readFileSync('fixtures/local.json', 'utf8')).customers.find((c) => c.key === 'D').customerId")

# Records, and returns 1 when it is over three (the app's cap did not hold), the OTP sends claimed for customer D on the running stack (read-only role).
check_d_sends() {
  local sends
  sends=$(docker exec -e PGPASSWORD="${QA_DB_PASSWORD:-dev-only-qa-db}" disputedesk-postgres-1 psql -h localhost -U qa_readonly -d disputedesk -At \
    -c "select count(*) from \"LinkingChallenges\" where \"CustomerId\" = '$customer_d' and \"OtpSentAtUtc\" is not null") || return 1
  echo "=== group $1: $sends OTP sends to customer D (cap 3 an hour) ==="
  [[ $sends -le 3 ]]
}

# <project> <production|raised>
run_group() {
  local project=$1 limits=$2 files=("${small_queue[@]}") result=0
  echo "=== group $project ($limits linking limits) ==="
  docker compose "${raised_limits[@]}" down -v --remove-orphans || return 1
  if [[ $limits == raised ]]; then files=("${raised_limits[@]}"); fi
  docker compose "${files[@]}" up -d --wait --build || return 1
  if [[ $limits == production ]]; then export E2E_LINKING_RATE_LIMITS=production; else unset E2E_LINKING_RATE_LIMITS; fi
  if [[ $project == regression ]]; then export E2E_LINKING_BUDGET_SECONDS=10; else unset E2E_LINKING_BUDGET_SECONDS; fi
  (cd e2e && npx playwright test --project="$project") || result=1
  check_d_sends "$project" || result=1
  return $result
}

failed=0
run_group linking-rate-limits production || failed=1
run_group linking-screens production || failed=1
run_group linking-queue production || failed=1
run_group linking-verify-a raised || failed=1
run_group linking-verify-b raised || failed=1
run_group linking-verify-c raised || failed=1
run_group regression raised || failed=1
docker compose "${raised_limits[@]}" stop
exit $failed
