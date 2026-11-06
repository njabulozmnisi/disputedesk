# The S3 bucket that holds envs/dev's state (AD-28). Applied once from the maintainer's machine with local state, and
# outside envs/dev, so destroying the environment leaves its state bucket (NFR-INFRA-5). The bucket follows Amazon S3,
# "Security best practices": public access blocked, versioning on, default encryption, ACLs off and HTTPS only.

terraform {
  required_version = "~> 1.11"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "6.71.0"
    }
  }
}

variable "aws_region" {
  description = "The same region as envs/dev."
  type        = string

  validation {
    condition     = contains(["af-south-1", "us-east-1", "us-west-2", "eu-west-1", "eu-central-1"], var.aws_region)
    error_message = "aws_region must be one of af-south-1, us-east-1, us-west-2, eu-west-1, eu-central-1."
  }
}

variable "state_bucket_name" {
  description = "A globally unique bucket name: 3 to 63 lower-case letters, digits and hyphens, no dots (Amazon S3, \"Bucket naming rules\")."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", var.state_bucket_name))
    error_message = "state_bucket_name must be 3 to 63 lower-case letters, digits and hyphens, starting and ending with a letter or digit."
  }
}

provider "aws" {
  region = var.aws_region
}

resource "aws_s3_bucket" "state" {
  bucket = var.state_bucket_name
}

resource "aws_s3_bucket_public_access_block" "state" {
  bucket                  = aws_s3_bucket.state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# Every state version is kept, so a bad write can be rolled back.
resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
  bucket = aws_s3_bucket.state.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# ACLs off: the bucket owner owns every object.
resource "aws_s3_bucket_ownership_controls" "state" {
  bucket = aws_s3_bucket.state.id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_policy" "state" {
  bucket = aws_s3_bucket.state.id
  # A JSON template whose names the infra tests' IamNamesTests check.
  policy = templatefile("${path.module}/state-bucket-policy.json.tftpl", {
    bucket_arn = aws_s3_bucket.state.arn
  })

  # A bucket policy is refused while public access blocking is still being applied to a new bucket.
  depends_on = [aws_s3_bucket_public_access_block.state]
}

output "state_bucket_name" {
  description = "envs/dev's -backend-config=\"bucket=...\"."
  value       = aws_s3_bucket.state.bucket
}
