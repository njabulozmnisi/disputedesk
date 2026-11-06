# Story 7.5, criterion 1: a plan of the state bucket against a mocked AWS provider, so no run reaches AWS or needs
# credentials (Terraform, "Tests" and "Mocks").
#   terraform init && terraform test

mock_provider "aws" {
  override_during = plan

  mock_resource "aws_s3_bucket" {
    defaults = {
      id  = "disputedesk-tfstate-example"
      arn = "arn:aws:s3:::disputedesk-tfstate-example"
    }
  }
}

variables {
  aws_region        = "eu-west-1"
  state_bucket_name = "disputedesk-tfstate-example"
}

run "criterion_1_the_state_bucket_is_private_versioned_encrypted_and_https_only" {
  command = plan

  assert {
    condition = (
      aws_s3_bucket_public_access_block.state.bucket == "disputedesk-tfstate-example" &&
      aws_s3_bucket_public_access_block.state.block_public_acls == true &&
      aws_s3_bucket_public_access_block.state.block_public_policy == true &&
      aws_s3_bucket_public_access_block.state.ignore_public_acls == true &&
      aws_s3_bucket_public_access_block.state.restrict_public_buckets == true
    )
    error_message = "Public access to the state bucket is not fully blocked."
  }

  assert {
    condition = (
      aws_s3_bucket_versioning.state.bucket == "disputedesk-tfstate-example" &&
      aws_s3_bucket_versioning.state.versioning_configuration[0].status == "Enabled"
    )
    error_message = "Versioning is not on."
  }

  assert {
    condition = (
      aws_s3_bucket_server_side_encryption_configuration.state.bucket == "disputedesk-tfstate-example" &&
      one(aws_s3_bucket_server_side_encryption_configuration.state.rule).apply_server_side_encryption_by_default[0].sse_algorithm == "AES256"
    )
    error_message = "Default encryption is not set."
  }

  assert {
    condition = (
      aws_s3_bucket_ownership_controls.state.bucket == "disputedesk-tfstate-example" &&
      aws_s3_bucket_ownership_controls.state.rule[0].object_ownership == "BucketOwnerEnforced"
    )
    error_message = "Object ownership is not BucketOwnerEnforced."
  }

  assert {
    condition = jsondecode(aws_s3_bucket_policy.state.policy).Statement == [{
      Sid       = "DenyInsecureTransport"
      Effect    = "Deny"
      Principal = "*"
      Action    = "s3:*"
      Resource  = ["arn:aws:s3:::disputedesk-tfstate-example", "arn:aws:s3:::disputedesk-tfstate-example/*"]
      Condition = { Bool = { "aws:SecureTransport" = "false" } }
    }]
    error_message = "The bucket policy does not deny every request over plain HTTP."
  }
}

run "a_bucket_name_with_a_dot_is_rejected" {
  command = plan

  variables {
    state_bucket_name = "disputedesk.tfstate"
  }

  expect_failures = [var.state_bucket_name]
}

run "a_bucket_name_with_upper_case_is_rejected" {
  command = plan

  variables {
    state_bucket_name = "DisputeDesk-tfstate"
  }

  expect_failures = [var.state_bucket_name]
}

run "a_bucket_name_of_2_characters_is_rejected" {
  command = plan

  variables {
    state_bucket_name = "ab"
  }

  expect_failures = [var.state_bucket_name]
}

run "a_bucket_name_of_64_characters_is_rejected" {
  command = plan

  variables {
    state_bucket_name = join("", [for index in range(64) : "a"])
  }

  expect_failures = [var.state_bucket_name]
}

run "a_bucket_name_ending_in_a_hyphen_is_rejected" {
  command = plan

  variables {
    state_bucket_name = "disputedesk-tfstate-"
  }

  expect_failures = [var.state_bucket_name]
}

# The edges are accepted: these plans succeed.
run "a_bucket_name_of_3_characters_is_accepted" {
  command = plan

  variables {
    state_bucket_name = "abc"
  }
}

run "a_bucket_name_of_63_characters_is_accepted" {
  command = plan

  variables {
    state_bucket_name = join("", [for index in range(63) : "a"])
  }
}

run "a_region_outside_the_five_is_rejected" {
  command = plan

  variables {
    aws_region = "sa-east-1"
  }

  expect_failures = [var.aws_region]
}
