terraform {
  # The bucket and region come from `terraform init -backend-config=...` (a backend block cannot read variables);
  # infra/terraform/README.md gives the command. use_lockfile is the S3 backend's native state locking.
  backend "s3" {
    key          = "envs/dev/terraform.tfstate"
    encrypt      = true
    use_lockfile = true
  }
}
