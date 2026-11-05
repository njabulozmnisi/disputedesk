# Story 7.5, criterion 5, story 7.15, criterion 1 (GitHub's immutable subject), and story 9.2, criterion 6 (the
# environment dev): a plan of the deploy-role module against a mocked AWS provider. The policies are JSON templates
# (story 8.16), so jsondecode here reads the real documents.
#   terraform init -backend=false && terraform test

mock_provider "aws" {
  override_during = plan

  mock_resource "aws_iam_openid_connect_provider" {
    defaults = { arn = "arn:aws:iam::0000-0000-0000:oidc-provider/token.actions.githubusercontent.com" }
  }

  mock_resource "aws_ssm_document" {
    defaults = { arn = "arn:aws:ssm:eu-west-1:0000-0000-0000:document/disputedesk-dev-deploy" }
  }
}

variables {
  name                       = "disputedesk-dev"
  aws_region                 = "eu-west-1"
  github_repository          = "example-owner/disputedesk"
  github_repository_owner_id = "123456"
  github_repository_id       = "456789"
  github_environment         = "dev"
  instance_arn               = "arn:aws:ec2:eu-west-1:0000-0000-0000:instance/i-0000-0000-000000001"
  clone_directory            = "/opt/disputedesk"
  ssm_path                   = "/disputedesk/dev"
}

run "criterion_5_the_role_trusts_only_this_repository_dev_environment" {
  command = plan

  module {
    source = "../../modules/deploy-role"
  }

  assert {
    condition = (
      aws_iam_openid_connect_provider.github.url == "https://token.actions.githubusercontent.com" &&
      tolist(aws_iam_openid_connect_provider.github.client_id_list) == tolist(["sts.amazonaws.com"]) &&
      aws_iam_role.deploy.max_session_duration == 3600
    )
    error_message = "The OIDC provider's audience or the role's session length is wrong."
  }

  assert {
    condition = jsondecode(aws_iam_role.deploy.assume_role_policy).Statement == [{
      Effect    = "Allow"
      Principal = { Federated = "arn:aws:iam::0000-0000-0000:oidc-provider/token.actions.githubusercontent.com" }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          "token.actions.githubusercontent.com:sub" = "repo:example-owner@123456/disputedesk@456789:environment:dev"
        }
      }
    }]
    error_message = "The trust policy is not exactly StringEquals on the audience and this repository's dev environment."
  }

  assert {
    condition     = !strcontains(aws_iam_role.deploy.assume_role_policy, "StringLike")
    error_message = "The trust policy uses StringLike."
  }
}

run "criterion_5_the_role_sends_only_the_deploy_document_to_the_instance" {
  command = plan

  module {
    source = "../../modules/deploy-role"
  }

  assert {
    condition = jsondecode(aws_iam_role_policy.deploy.policy).Statement == [
      {
        Sid      = "SendDeployCommand"
        Effect   = "Allow"
        Action   = "ssm:SendCommand"
        Resource = ["arn:aws:ec2:eu-west-1:0000-0000-0000:instance/i-0000-0000-000000001", "arn:aws:ssm:eu-west-1:0000-0000-0000:document/disputedesk-dev-deploy"]
      },
      {
        Sid      = "ReadDeployResult"
        Effect   = "Allow"
        Action   = ["ssm:GetCommandInvocation", "ssm:ListCommandInvocations"]
        Resource = "*"
      },
    ]
    error_message = "The deploy role may send something other than the deploy document to the instance."
  }

  assert {
    condition     = !strcontains(aws_iam_role_policy.deploy.policy, "AWS-RunShellScript")
    error_message = "The deploy role may run AWS-RunShellScript."
  }

  assert {
    condition     = aws_ssm_document.deploy.document_type == "Command" && jsondecode(aws_ssm_document.deploy.content).schemaVersion == "2.2"
    error_message = "The deploy document is not a schema 2.2 Command document."
  }

  assert {
    condition = jsondecode(aws_ssm_document.deploy.content).parameters == {
      sha              = { type = "String", description = "The full commit SHA to deploy.", allowedPattern = "^[0-9a-f]{40}$", interpolationType = "ENV_VAR" }
      apiImage         = { type = "String", description = "A published image by digest.", allowedPattern = "^ghcr\\.io/example-owner/disputedesk-[a-z-]+@sha256:[0-9a-f]{64}$", interpolationType = "ENV_VAR" }
      workerImage      = { type = "String", description = "A published image by digest.", allowedPattern = "^ghcr\\.io/example-owner/disputedesk-[a-z-]+@sha256:[0-9a-f]{64}$", interpolationType = "ENV_VAR" }
      gatewayImage     = { type = "String", description = "A published image by digest.", allowedPattern = "^ghcr\\.io/example-owner/disputedesk-[a-z-]+@sha256:[0-9a-f]{64}$", interpolationType = "ENV_VAR" }
      coreBankingImage = { type = "String", description = "A published image by digest.", allowedPattern = "^ghcr\\.io/example-owner/disputedesk-[a-z-]+@sha256:[0-9a-f]{64}$", interpolationType = "ENV_VAR" }
      provisionImage   = { type = "String", description = "A published image by digest.", allowedPattern = "^ghcr\\.io/example-owner/disputedesk-[a-z-]+@sha256:[0-9a-f]{64}$", interpolationType = "ENV_VAR" }
    }
    error_message = "The deploy document's parameters do not carry their allowedPattern and ENV_VAR interpolation."
  }

  assert {
    condition = jsondecode(aws_ssm_document.deploy.content).mainSteps == [{
      action = "aws:runShellScript"
      name   = "deploy"
      inputs = {
        timeoutSeconds   = 1800
        workingDirectory = "/opt/disputedesk"
        runCommand = [
          "set -eu",
          "git fetch --quiet origin",
          "git merge-base --is-ancestor \"$SSM_sha\" origin/main",
          "git checkout --quiet --detach \"$SSM_sha\"",
          "AWS_REGION='eu-west-1' SSM_PATH='/disputedesk/dev' infra/aws/deploy.sh \"$SSM_apiImage\" \"$SSM_workerImage\" \"$SSM_gatewayImage\" \"$SSM_coreBankingImage\" \"$SSM_provisionImage\"",
        ]
      }
    }]
    error_message = "The document's one step does not refuse a commit off main, then check out the SHA and run deploy.sh from the clone within 1800 seconds."
  }

  assert {
    condition     = !strcontains(aws_ssm_document.deploy.content, "{{")
    error_message = "The deploy document splices a parameter into the script instead of reading SSM_<name>."
  }
}

# GitHub owners may hold capitals; GHCR names are lower case, so the pattern names the owner in lower case. The subject
# keeps GitHub's case, because StringEquals is case-sensitive.
run "criterion_5_the_image_pattern_names_this_repository_owner_in_lower_case" {
  command = plan

  module {
    source = "../../modules/deploy-role"
  }

  variables {
    github_repository = "Example-Owner/disputedesk"
  }

  assert {
    condition     = jsondecode(aws_ssm_document.deploy.content).parameters.apiImage.allowedPattern == "^ghcr\\.io/example-owner/disputedesk-[a-z-]+@sha256:[0-9a-f]{64}$"
    error_message = "The image pattern does not name this repository's owner in lower case."
  }

  assert {
    condition     = jsondecode(aws_iam_role.deploy.assume_role_policy).Statement[0].Condition.StringEquals["token.actions.githubusercontent.com:sub"] == "repo:Example-Owner@123456/disputedesk@456789:environment:dev"
    error_message = "The trust policy's subject does not keep the owner's case or name this repository's dev environment."
  }
}
