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
}
