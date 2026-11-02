# Story 7.15, criterion 4: CI's oidc-subject job checks that the subject the deploy role trusts equals the sub of a token
# GitHub issued to this repository (the oidc-token job's output). A plan of the deploy-role module against a mocked AWS
# provider. The repository, its two IDs and the expected subject all come from -var, so no variables block here may
# name them (a file's variables block overrides -var). From infra/terraform/envs/dev:
#   terraform init -backend=false -test-directory=tests-oidc
#   terraform test -test-directory=tests-oidc -var github_repository=<owner>/<name> \
#     -var github_repository_owner_id=<id> -var github_repository_id=<id> -var expected_subject=<sub>

variable "expected_subject" {
  type = string
}

# The provider's ARN is in the trust policy, so it gets a value at plan time, as in tests/deploy_role.tftest.hcl.
mock_provider "aws" {
  override_during = plan

  mock_resource "aws_iam_openid_connect_provider" {
    defaults = { arn = "arn:aws:iam::0000-0000-0000:oidc-provider/token.actions.githubusercontent.com" }
  }
}

run "the_trust_policy_names_the_subject_github_issued" {
  command = plan

  module {
    source = "../../modules/deploy-role"
  }

  variables {
    name            = "disputedesk-dev"
    aws_region      = "eu-west-1"
    instance_arn    = "arn:aws:ec2:eu-west-1:0000-0000-0000:instance/i-0000-0000-000000001"
    clone_directory = "/opt/disputedesk"
    ssm_path        = "/disputedesk/dev"
  }

  assert {
    condition     = jsondecode(aws_iam_role.deploy.assume_role_policy).Statement[0].Condition.StringEquals["token.actions.githubusercontent.com:sub"] == var.expected_subject
    error_message = "The subject the deploy role trusts is not the sub of the token GitHub issued to this repository."
  }
}
