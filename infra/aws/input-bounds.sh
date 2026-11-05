# shellcheck shell=bash
# The inputs of infra/aws/compose.yml and the bound each value must meet (story 7.4, Inputs; AD-26). Sourced, not run:
# ComposeRenderTests (infra/tests) checks compose.test.env with it, and the deploy script checks .env with it before any
# docker call, so both use the same patterns.
#
# Every secret is generated from [A-Za-z0-9], never chosen by hand: `;` would add Npgsql connection-string keywords,
# `?`, `#` and `%` break Zitadel's DSN URL, and `$`, quotes and spaces break .env interpolation. The Zitadel admin
# password also needs `-`, because Zitadel's default password complexity policy requires a symbol.

# shellcheck disable=SC2034 # read by the scripts that source this file
AWS_INPUTS=(
  APP_DB_PASSWORD ZITADEL_DB_PASSWORD LOGIN_SESSION_COOKIE_SECRET ZITADEL_MASTERKEY ZITADEL_ADMIN_PASSWORD
  LINKING_OTP_HMAC_KEY MAILPIT_UI_AUTH GATEWAY_ORIGIN_VERIFY
  APP_HOST AUTH_HOST MAILPIT_HOST DB_HOST AWS_REGION OTEL_LOG_GROUP DATAPROTECTION_SSM_PATH DATAPROTECTION_KMS_KEY_ID
  RDS_CA_BUNDLE GATEWAY_REALIP_CONF API_IMAGE WORKER_IMAGE GATEWAY_IMAGE CORE_BANKING_IMAGE PROVISION_IMAGE
)

# A DNS host name: lower-case labels of letters, digits and inner hyphens, at least two of them, no scheme or port.
HOST_NAME='^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$'

# A KMS key ID (the key's UUID, lower-case hex).
KMS_KEY_UUID='[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'

# within_bounds NAME VALUE: exit status 0 when VALUE meets NAME's bound.
within_bounds() {
  local name=$1 value=$2
  case $name in
    APP_DB_PASSWORD | ZITADEL_DB_PASSWORD | LOGIN_SESSION_COOKIE_SECRET | GATEWAY_ORIGIN_VERIFY)
      [[ $value =~ ^[A-Za-z0-9]{32,128}$ ]] ;;
    ZITADEL_MASTERKEY)
      [[ $value =~ ^[A-Za-z0-9]{32}$ ]] ;;
    ZITADEL_ADMIN_PASSWORD)
      [[ $value =~ ^[A-Za-z0-9-]{32,128}$ && $value =~ [A-Z] && $value =~ [a-z] && $value =~ [0-9] && $value == *-* ]] ;;
    LINKING_OTP_HMAC_KEY)
      # LinkingOptions' bound: base64 (RFC 4648 section 4, padded) for at least 32 bytes.
      [[ $value =~ ^[A-Za-z0-9+/]+={0,2}$ ]] && (( ${#value} % 4 == 0 )) \
        && (( $(printf '%s' "$value" | base64 -d 2>/dev/null | wc -c) >= 32 )) ;;
    MAILPIT_UI_AUTH)
      [[ $value =~ ^[A-Za-z0-9]{1,32}:[A-Za-z0-9]{32,128}$ ]] ;;
    APP_HOST | AUTH_HOST | MAILPIT_HOST | DB_HOST)
      (( ${#value} <= 253 )) && [[ $value =~ $HOST_NAME ]] ;;
    AWS_REGION)
      [[ $value =~ ^[a-z]{2}(-[a-z]+)+-[0-9]$ ]] ;;
    OTEL_LOG_GROUP)
      # CloudWatch Logs' log group name rules: 1 to 512 of these characters (a length test, because 512 is over the
      # largest repeat count POSIX regular expressions must support).
      (( ${#value} <= 512 )) && [[ $value =~ ^[A-Za-z0-9_./#-]+$ ]] ;;
    DATAPROTECTION_SSM_PATH)
      # Starts with / and has no trailing /, as DataProtection:SsmParameterPath's validation requires.
      [[ $value =~ ^(/[A-Za-z0-9_.-]+)+$ ]] ;;
    DATAPROTECTION_KMS_KEY_ID)
      # The customer-managed key by its ARN, or by its key ID alone (the ARN's last part), never an alias, which could
      # be SSM's default alias/aws/ssm. Both name the one key; the key ID form keeps account IDs out of test data.
      [[ $value =~ ^(arn:aws:kms:[a-z0-9-]+:[0-9]{12}:key/)?$KMS_KEY_UUID$ ]] ;;
    RDS_CA_BUNDLE | GATEWAY_REALIP_CONF)
      [[ $value =~ ^(/[A-Za-z0-9_.-]+)+$ ]] ;;
    API_IMAGE | WORKER_IMAGE | GATEWAY_IMAGE | CORE_BANKING_IMAGE | PROVISION_IMAGE)
      # A published GHCR image by digest (AD-32), never by tag.
      [[ $value =~ ^ghcr\.io/[a-z0-9-]+/[a-z0-9._-]+@sha256:[0-9a-f]{64}$ ]] ;;
    *)
      return 1 ;;
  esac
}
