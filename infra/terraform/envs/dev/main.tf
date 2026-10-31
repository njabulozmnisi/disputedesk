# The one AWS dev environment (AD-28). Planned and applied only from the maintainer's machine, by the procedure in
# infra/terraform/README.md; CI runs only fmt, validate and the mocked tests.

provider "aws" {
  region = var.aws_region
}

data "aws_caller_identity" "current" {}

locals {
  name = "disputedesk-dev"

  # The fixed directory the instance clones the repository into and the deploy document runs from.
  clone_directory = "/opt/disputedesk"
}

# The one customer-managed key for every SecureString under both SSM paths, so the instance role's KMS rights name one
# key (AWS KMS, "How AWS Systems Manager Parameter Store uses AWS KMS"). Its default key policy lets IAM policies grant
# its use. A destroyed key waits the shortest deletion window, 7 days (AWS KMS, "Deleting AWS KMS keys").
resource "aws_kms_key" "ssm" {
  description             = "${local.name} SSM SecureStrings"
  enable_key_rotation     = true
  deletion_window_in_days = 7
}

module "network" {
  source = "../../modules/network"

  name = local.name
}

module "compute" {
  source = "../../modules/compute"

  name                       = local.name
  aws_region                 = var.aws_region
  account_id                 = data.aws_caller_identity.current.account_id
  vpc_id                     = module.network.vpc_id
  subnet_id                  = module.network.public_subnet_id
  database_security_group_id = module.database.security_group_id
  github_repository          = var.github_repository
  clone_directory            = local.clone_directory
  ssm_path                   = var.ssm_path
  dataprotection_ssm_path    = var.dataprotection_ssm_path
  kms_key_arn                = aws_kms_key.ssm.arn
}

module "database" {
  source = "../../modules/database"

  name                       = local.name
  vpc_id                     = module.network.vpc_id
  private_subnet_ids         = module.network.private_subnet_ids
  instance_security_group_id = module.compute.security_group_id
  ssm_path                   = var.ssm_path
  kms_key_arn                = aws_kms_key.ssm.arn
}

module "edge" {
  source = "../../modules/edge"

  name               = local.name
  origin_domain_name = module.compute.public_dns
}

module "deploy_role" {
  source = "../../modules/deploy-role"

  name              = local.name
  aws_region        = var.aws_region
  github_repository = var.github_repository
  instance_arn      = module.compute.instance_arn
  clone_directory   = local.clone_directory
  ssm_path          = var.ssm_path
}

module "observability" {
  source = "../../modules/observability"

  name       = local.name
  aws_region = var.aws_region
  account_id = data.aws_caller_identity.current.account_id
}

module "budget" {
  source = "../../modules/budget"

  name        = local.name
  limit_usd   = var.budget_limit_usd
  alert_email = var.budget_alert_email
}

# What the deploy script reads from ssm_path besides the secrets a person creates (infra/terraform/README.md) and the
# master password the database module writes. Names and bounds: infra/deploy/aws/input-bounds.sh.
resource "aws_ssm_parameter" "deploy_value" {
  for_each = {
    APP_HOST                  = module.edge.domain_names["app"]
    AUTH_HOST                 = module.edge.domain_names["auth"]
    MAILPIT_HOST              = module.edge.domain_names["mail"]
    DB_HOST                   = module.database.address
    OTEL_LOG_GROUP            = module.observability.log_group_name
    DATAPROTECTION_SSM_PATH   = var.dataprotection_ssm_path
    DATAPROTECTION_KMS_KEY_ID = aws_kms_key.ssm.arn
  }

  name  = "${var.ssm_path}/${each.key}"
  type  = "String"
  value = each.value
}

# shortcut: the origin-verify value is in state (the bucket is private, versioned and encrypted), because CloudFront's
# origin header needs it in the plan; move it to a write-only argument when that header accepts an ephemeral value.
resource "aws_ssm_parameter" "origin_verify" {
  name   = "${var.ssm_path}/GATEWAY_ORIGIN_VERIFY"
  type   = "SecureString"
  key_id = aws_kms_key.ssm.arn
  value  = module.edge.origin_verify
}
