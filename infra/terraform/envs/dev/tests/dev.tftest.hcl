# Story 7.5, the envs/dev root: what it writes to SSM for the deploy script (criterion 4), its key (criterion 7), its
# outputs for the repository variables, the Elastic IP origin, and the bounds of every input variable. A plan
# against a mocked AWS provider; the random provider runs for real (it works offline).
#   terraform init -backend=false && terraform test

mock_provider "aws" {
  override_during = plan

  override_data {
    target = data.aws_caller_identity.current
    values = { account_id = "0000-0000-0000" }
  }

  override_data {
    target = module.network.data.aws_availability_zones.available
    values = { names = ["eu-west-1a", "eu-west-1b", "eu-west-1c"] }
  }

  override_resource {
    target = aws_kms_key.ssm
    values = { arn = "arn:aws:kms:eu-west-1:0000-0000-0000:key/00000000-0000-0000-0000-00000000000a" }
  }

  override_resource {
    target = module.compute.aws_security_group.instance
    values = { id = "sg-0000-0000-000000001" }
  }

  override_resource {
    target = module.database.aws_security_group.database
    values = { id = "sg-0000-0000-000000002" }
  }

  override_resource {
    target = module.compute.aws_instance.this
    values = { id = "i-0000-0000-000000001", arn = "arn:aws:ec2:eu-west-1:0000-0000-0000:instance/i-0000-0000-000000001" }
  }

  override_resource {
    target = module.compute.aws_eip.this
    values = { public_dns = "ec2-192-0-2-10.eu-west-1.compute.amazonaws.com" }
  }

  override_resource {
    target = module.database.aws_db_instance.this
    values = { address = "disputedesk-dev.abcdefghijkl.eu-west-1.rds.amazonaws.com" }
  }

  override_resource {
    target = module.edge.aws_cloudfront_distribution.this["app"]
    values = { domain_name = "d111111abcdef8.cloudfront.net" }
  }

  override_resource {
    target = module.edge.aws_cloudfront_distribution.this["auth"]
    values = { domain_name = "d222222abcdef8.cloudfront.net" }
  }

  override_resource {
    target = module.edge.aws_cloudfront_distribution.this["mail"]
    values = { domain_name = "d333333abcdef8.cloudfront.net" }
  }

  override_resource {
    target = module.deploy_role.aws_iam_role.deploy
    values = { arn = "arn:aws:iam::0000-0000-0000:role/disputedesk-dev-deploy" }
  }
}

override_resource {
  target          = module.edge.random_password.origin_verify
  override_during = plan
  values          = { result = "TestOnlyOriginVerifyValueNotASecret0" }
}

variables {
  aws_region                 = "eu-west-1"
  github_repository          = "example-owner/disputedesk"
  github_repository_owner_id = "123456"
  github_repository_id       = "456789"
  budget_limit_usd           = 10
  budget_alert_email         = "budget@example.com"
}

run "criterion_4_terraform_writes_the_deploy_values_under_ssm_path" {
  command = plan

  assert {
    condition = { for name, parameter in aws_ssm_parameter.deploy_value : name => parameter.value } == {
      APP_HOST                  = "d111111abcdef8.cloudfront.net"
      AUTH_HOST                 = "d222222abcdef8.cloudfront.net"
      MAILPIT_HOST              = "d333333abcdef8.cloudfront.net"
      DB_HOST                   = "disputedesk-dev.abcdefghijkl.eu-west-1.rds.amazonaws.com"
      OTEL_LOG_GROUP            = "/disputedesk-dev/otel"
      DATAPROTECTION_SSM_PATH   = "/disputedesk-dataprotection/dev"
      DATAPROTECTION_KMS_KEY_ID = "arn:aws:kms:eu-west-1:0000-0000-0000:key/00000000-0000-0000-0000-00000000000a"
    }
    error_message = "The values Terraform writes are not the three distributions' hosts, the RDS host, the log group, the key-ring path and the key's ARN."
  }

  assert {
    condition = alltrue([for name, parameter in aws_ssm_parameter.deploy_value :
      parameter.name == "/disputedesk/dev/${name}" && parameter.type == "String"
    ])
    error_message = "A deploy value is not a String named after its input under ssm_path."
  }

  assert {
    condition = (
      aws_ssm_parameter.origin_verify.name == "/disputedesk/dev/GATEWAY_ORIGIN_VERIFY" &&
      aws_ssm_parameter.origin_verify.type == "SecureString" &&
      aws_ssm_parameter.origin_verify.key_id == "arn:aws:kms:eu-west-1:0000-0000-0000:key/00000000-0000-0000-0000-00000000000a" &&
      aws_ssm_parameter.origin_verify.value == "TestOnlyOriginVerifyValueNotASecret0"
    )
    error_message = "GATEWAY_ORIGIN_VERIFY is not the origin-verify value as a SecureString under the customer-managed key."
  }

  assert {
    condition = (
      output.deploy_role_arn == "arn:aws:iam::0000-0000-0000:role/disputedesk-dev-deploy" &&
      output.aws_region == "eu-west-1" &&
      output.deploy_instance_id == "i-0000-0000-000000001" &&
      output.deploy_document_name == "disputedesk-dev-deploy"
    )
    error_message = "The outputs for the four repository variables are wrong."
  }
}

run "criterion_7_one_rotating_key_with_a_7_day_deletion_window" {
  command = plan

  assert {
    condition     = aws_kms_key.ssm.enable_key_rotation == true && aws_kms_key.ssm.deletion_window_in_days == 7
    error_message = "The KMS key does not rotate or does not use the 7-day deletion window."
  }
}

# The origin CloudFront is given is the Elastic IP's DNS name (overridden above), not the instance's pre-EIP name.
run "criterion_4_the_origin_is_the_elastic_ip_public_dns" {
  command = plan

  assert {
    condition     = module.compute.public_dns == "ec2-192-0-2-10.eu-west-1.compute.amazonaws.com"
    error_message = "The compute module's public_dns is not the Elastic IP's DNS name."
  }
}

# Input bounds: each variable rejects a value just outside its validation, before anything is planned.

run "aws_region_outside_the_four_regions_is_rejected" {
  command = plan

  variables {
    aws_region = "af-south-1"
  }

  expect_failures = [var.aws_region]
}

run "github_repository_without_an_owner_is_rejected" {
  command = plan

  variables {
    github_repository = "disputedesk"
  }

  expect_failures = [var.github_repository]
}

run "github_repository_with_a_quote_is_rejected" {
  command = plan

  variables {
    github_repository = "example-owner/dispute\"desk"
  }

  expect_failures = [var.github_repository]
}

# Story 7.15, criterion 2: each GitHub ID is 1 to 19 digits with no leading zero, so the subject renders it digit for
# digit.

run "github_repository_owner_id_empty_is_rejected" {
  command = plan

  variables {
    github_repository_owner_id = ""
  }

  expect_failures = [var.github_repository_owner_id]
}

run "github_repository_owner_id_of_0_is_rejected" {
  command = plan

  variables {
    github_repository_owner_id = "0"
  }

  expect_failures = [var.github_repository_owner_id]
}

run "github_repository_owner_id_with_a_leading_zero_is_rejected" {
  command = plan

  variables {
    github_repository_owner_id = "0123"
  }

  expect_failures = [var.github_repository_owner_id]
}

run "github_repository_owner_id_with_a_letter_is_rejected" {
  command = plan

  variables {
    github_repository_owner_id = "12a"
  }

  expect_failures = [var.github_repository_owner_id]
}

run "github_repository_owner_id_negative_is_rejected" {
  command = plan

  variables {
    github_repository_owner_id = "-1"
  }

  expect_failures = [var.github_repository_owner_id]
}

run "github_repository_owner_id_of_20_digits_is_rejected" {
  command = plan

  variables {
    github_repository_owner_id = "12345678901234567890"
  }

  expect_failures = [var.github_repository_owner_id]
}

run "github_repository_owner_id_of_1_is_accepted" {
  command = plan

  variables {
    github_repository_owner_id = "1"
  }
}

run "github_repository_owner_id_of_19_digits_is_accepted" {
  command = plan

  variables {
    github_repository_owner_id = "1234567890123456789"
  }
}

run "github_repository_id_empty_is_rejected" {
  command = plan

  variables {
    github_repository_id = ""
  }

  expect_failures = [var.github_repository_id]
}

run "github_repository_id_of_0_is_rejected" {
  command = plan

  variables {
    github_repository_id = "0"
  }

  expect_failures = [var.github_repository_id]
}

run "github_repository_id_with_a_leading_zero_is_rejected" {
  command = plan

  variables {
    github_repository_id = "0123"
  }

  expect_failures = [var.github_repository_id]
}

run "github_repository_id_with_a_letter_is_rejected" {
  command = plan

  variables {
    github_repository_id = "12a"
  }

  expect_failures = [var.github_repository_id]
}

run "github_repository_id_negative_is_rejected" {
  command = plan

  variables {
    github_repository_id = "-1"
  }

  expect_failures = [var.github_repository_id]
}

run "github_repository_id_of_20_digits_is_rejected" {
  command = plan

  variables {
    github_repository_id = "12345678901234567890"
  }

  expect_failures = [var.github_repository_id]
}

run "github_repository_id_of_1_is_accepted" {
  command = plan

  variables {
    github_repository_id = "1"
  }
}

run "github_repository_id_of_19_digits_is_accepted" {
  command = plan

  variables {
    github_repository_id = "1234567890123456789"
  }
}

run "budget_limit_of_0_is_rejected" {
  command = plan

  variables {
    budget_limit_usd = 0
  }

  expect_failures = [var.budget_limit_usd]
}

run "budget_limit_of_101_is_rejected" {
  command = plan

  variables {
    budget_limit_usd = 101
  }

  expect_failures = [var.budget_limit_usd]
}

run "budget_limit_with_a_fraction_is_rejected" {
  command = plan

  variables {
    budget_limit_usd = 10.5
  }

  expect_failures = [var.budget_limit_usd]
}

# The edges are accepted: these plans succeed.
run "budget_limit_of_1_is_accepted" {
  command = plan

  variables {
    budget_limit_usd = 1
  }
}

run "budget_limit_of_100_is_accepted" {
  command = plan

  variables {
    budget_limit_usd = 100
  }
}

run "budget_alert_email_of_254_characters_is_accepted" {
  command = plan

  variables {
    budget_alert_email = "${join("", [for index in range(242) : "a"])}@example.com"
  }
}

run "budget_alert_email_without_an_at_sign_is_rejected" {
  command = plan

  variables {
    budget_alert_email = "budget.example.com"
  }

  expect_failures = [var.budget_alert_email]
}

run "budget_alert_email_with_two_addresses_is_rejected" {
  command = plan

  variables {
    budget_alert_email = "budget@example.com other@example.com"
  }

  expect_failures = [var.budget_alert_email]
}

run "budget_alert_email_over_254_characters_is_rejected" {
  command = plan

  variables {
    budget_alert_email = "${join("", [for index in range(243) : "a"])}@example.com"
  }

  expect_failures = [var.budget_alert_email]
}

run "ssm_path_with_a_trailing_slash_is_rejected" {
  command = plan

  variables {
    ssm_path = "/disputedesk/dev/"
  }

  expect_failures = [var.ssm_path]
}

run "ssm_path_without_a_leading_slash_is_rejected" {
  command = plan

  variables {
    ssm_path = "disputedesk/dev"
  }

  expect_failures = [var.ssm_path]
}

run "dataprotection_ssm_path_under_ssm_path_is_rejected" {
  command = plan

  variables {
    dataprotection_ssm_path = "/disputedesk/dev/keys"
  }

  expect_failures = [var.dataprotection_ssm_path]
}

run "dataprotection_ssm_path_equal_to_ssm_path_is_rejected" {
  command = plan

  variables {
    dataprotection_ssm_path = "/disputedesk/dev"
  }

  expect_failures = [var.dataprotection_ssm_path]
}

run "ssm_path_under_dataprotection_ssm_path_is_rejected" {
  command = plan

  variables {
    ssm_path = "/disputedesk-dataprotection/dev/deploy"
  }

  expect_failures = [var.dataprotection_ssm_path]
}

run "a_sibling_path_sharing_a_prefix_is_accepted" {
  command = plan

  variables {
    dataprotection_ssm_path = "/disputedesk/dev-keys"
  }

  assert {
    condition     = aws_ssm_parameter.deploy_value["DATAPROTECTION_SSM_PATH"].value == "/disputedesk/dev-keys"
    error_message = "A key-ring path beside ssm_path, not under it, is not accepted."
  }
}
