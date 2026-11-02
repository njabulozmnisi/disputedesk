# The role .github/workflows/deploy.yml assumes through GitHub's OIDC provider, with no long-lived key (AD-28; GitHub,
# "Configuring OpenID Connect in Amazon Web Services"), and the one Run Command document it may send.

variable "name" {
  description = "The prefix for every resource name."
  type        = string
}

variable "aws_region" {
  type = string
}

variable "github_repository" {
  description = "<owner>/<name>; only its main branch may assume the role."
  type        = string
}

variable "github_repository_owner_id" {
  description = "The numeric ID of the repository's owner, as digits."
  type        = string
}

variable "github_repository_id" {
  description = "The repository's numeric ID, as digits."
  type        = string
}

variable "instance_arn" {
  type = string
}

variable "clone_directory" {
  type = string
}

variable "ssm_path" {
  type = string
}

locals {
  # GitHub's immutable subject of a job on this repository's main branch with no environment,
  # repo:<owner>@<owner-id>/<name>@<repo-id>:ref:refs/heads/main (GitHub, "OpenID Connect reference", immutable subject
  # claims). The owner and name keep GitHub's case: StringEquals is case-sensitive.
  oidc_subject = format("repo:%s@%s/%s@%s:ref:refs/heads/main",
    split("/", var.github_repository)[0], var.github_repository_owner_id,
    split("/", var.github_repository)[1], var.github_repository_id,
  )

  # A published GHCR image by digest (AD-32), the shape infra/deploy/aws/input-bounds.sh checks, narrowed to this repository's
  # owner. GHCR names are lower case; the owner's characters ([A-Za-z0-9-], envs/dev's validation) need no escaping.
  image_owner   = lower(split("/", var.github_repository)[0])
  image_pattern = "^ghcr\\.io/${local.image_owner}/disputedesk-[a-z-]+@sha256:[0-9a-f]{64}$"

  # The document's image parameters, in deploy.sh's argument order.
  image_parameters = ["apiImage", "workerImage", "gatewayImage", "coreBankingImage", "zitadelImage"]
}

resource "aws_iam_openid_connect_provider" "github" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]
}

resource "aws_iam_role" "deploy" {
  name                 = "${var.name}-deploy"
  max_session_duration = 3600

  # Exact matches only: the audience, and the subject of a job on this repository's main branch with no environment.
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = aws_iam_openid_connect_provider.github.arn }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          "token.actions.githubusercontent.com:sub" = local.oidc_subject
        }
      }
    }]
  })
}

# The deploy, as a Command document: its parameters must match their patterns before the command is sent, and reach the
# shell as environment variables SSM_<name>, never spliced into the script (AWS Systems Manager, "Data elements and
# parameters", allowedPattern and interpolationType).
resource "aws_ssm_document" "deploy" {
  name            = "${var.name}-deploy"
  document_type   = "Command"
  document_format = "JSON"

  content = jsonencode({
    schemaVersion = "2.2"
    description   = "Check out a commit of the repository and run infra/deploy/aws/deploy.sh with its five image digests."
    parameters = merge(
      {
        sha = {
          type              = "String"
          description       = "The full commit SHA to deploy."
          allowedPattern    = "^[0-9a-f]{40}$"
          interpolationType = "ENV_VAR"
        }
      },
      {
        for parameter in local.image_parameters : parameter => {
          type              = "String"
          description       = "A published image by digest."
          allowedPattern    = local.image_pattern
          interpolationType = "ENV_VAR"
        }
      },
    )
    mainSteps = [{
      action = "aws:runShellScript"
      name   = "deploy"
      inputs = {
        timeoutSeconds   = 1800
        workingDirectory = var.clone_directory
        # Only a commit on main runs as root here: merge-base fails, and set -e stops the step, for any other commit
        # (git, "git merge-base --is-ancestor").
        runCommand = [
          "set -eu",
          "git fetch --quiet origin",
          "git merge-base --is-ancestor \"$SSM_sha\" origin/main",
          "git checkout --quiet --detach \"$SSM_sha\"",
          "AWS_REGION='${var.aws_region}' SSM_PATH='${var.ssm_path}' infra/deploy/aws/deploy.sh ${join(" ", [for parameter in local.image_parameters : "\"$SSM_${parameter}\""])}",
        ]
      }
    }]
  })
}

# Only this document, only on this instance; never AWS-RunShellScript, which would run any command as root. The
# invocation reads have no resource-level scope.
resource "aws_iam_role_policy" "deploy" {
  name = "${var.name}-deploy"
  role = aws_iam_role.deploy.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "SendDeployCommand"
        Effect   = "Allow"
        Action   = "ssm:SendCommand"
        Resource = [var.instance_arn, aws_ssm_document.deploy.arn]
      },
      {
        Sid      = "ReadDeployResult"
        Effect   = "Allow"
        Action   = ["ssm:GetCommandInvocation", "ssm:ListCommandInvocations"]
        Resource = "*"
      },
    ]
  })
}

output "role_arn" {
  value = aws_iam_role.deploy.arn
}

output "document_name" {
  value = aws_ssm_document.deploy.name
}
