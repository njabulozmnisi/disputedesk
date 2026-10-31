terraform {
  # 1.11 or later: write-only arguments (the RDS master password) and override_during in the mocked tests.
  required_version = "~> 1.11"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "6.71.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "3.9.1"
    }
  }

  # The bucket and region come from `terraform init -backend-config=...` (a backend block cannot read variables);
  # infra/terraform/README.md gives the command. use_lockfile is the S3 backend's native state locking.
  backend "s3" {
    key          = "envs/dev/terraform.tfstate"
    encrypt      = true
    use_lockfile = true
  }
}
