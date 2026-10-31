variable "aws_region" {
  description = "The region for every resource but the budget (AWS Budgets is global)."
  type        = string

  validation {
    # Each offers c7i-flex.large, db.t4g.micro and X-Ray Transaction Search.
    condition     = contains(["us-east-1", "us-west-2", "eu-west-1", "eu-central-1"], var.aws_region)
    error_message = "aws_region must be one of us-east-1, us-west-2, eu-west-1, eu-central-1."
  }
}

variable "github_repository" {
  description = "The repository as <owner>/<name>: the instance clones it, and only its main branch may assume the deploy role."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$", var.github_repository))
    error_message = "github_repository must be <owner>/<name>, for example octo-org/disputedesk."
  }
}

variable "budget_limit_usd" {
  description = "The monthly cost budget in US dollars, a whole number from 1 to 100."
  type        = number

  validation {
    condition     = var.budget_limit_usd == floor(var.budget_limit_usd) && var.budget_limit_usd >= 1 && var.budget_limit_usd <= 100
    error_message = "budget_limit_usd must be a whole number from 1 to 100."
  }
}

variable "budget_alert_email" {
  description = "The one address the budget alerts go to. Personal data: pass it as TF_VAR_budget_alert_email, never in a committed file."
  type        = string
  sensitive   = true

  validation {
    # 254 characters is the longest address SMTP carries (RFC 5321, section 4.5.3.1).
    condition     = length(var.budget_alert_email) <= 254 && can(regex("^[^@\\s]+@[^@\\s]+$", var.budget_alert_email))
    error_message = "budget_alert_email must be one email address."
  }
}

variable "ssm_path" {
  description = "The SSM path the deploy script reads, starting with / and with no trailing /."
  type        = string
  default     = "/disputedesk/dev"

  validation {
    condition     = can(regex("^(/[A-Za-z0-9_.-]+)+$", var.ssm_path))
    error_message = "ssm_path must start with / and have no trailing /, with letters, digits, _, . and - between."
  }
}

variable "dataprotection_ssm_path" {
  description = "The SSM path of the Api's Data Protection key ring, outside ssm_path, so the deploy never reads it."
  type        = string
  default     = "/disputedesk-dataprotection/dev"

  validation {
    condition     = can(regex("^(/[A-Za-z0-9_.-]+)+$", var.dataprotection_ssm_path))
    error_message = "dataprotection_ssm_path must start with / and have no trailing /, with letters, digits, _, . and - between."
  }

  # Neither path may hold the other (or equal it): GetParametersByPath on one would return the other's parameters.
  validation {
    condition = (
      !startswith("${var.dataprotection_ssm_path}/", "${var.ssm_path}/") &&
      !startswith("${var.ssm_path}/", "${var.dataprotection_ssm_path}/")
    )
    error_message = "dataprotection_ssm_path and ssm_path must not be the same path or one under the other."
  }
}
