# The values of the four repository variables .github/workflows/deploy.yml reads; infra/terraform/README.md sets
# AWS_DEPLOY_ROLE_ARN, AWS_REGION, DEPLOY_INSTANCE_ID and DEPLOY_DOCUMENT_NAME from them.

output "deploy_role_arn" {
  value = module.deploy_role.role_arn
}

output "aws_region" {
  value = var.aws_region
}

output "deploy_instance_id" {
  value = module.compute.instance_id
}

output "deploy_document_name" {
  value = module.deploy_role.document_name
}
