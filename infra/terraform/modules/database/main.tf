# RDS PostgreSQL for both databases (AD-28): private, encrypted, TLS required, one day of backups, and nothing that
# stops `terraform destroy`. The deploy script creates the roles and databases on the first deploy.

variable "name" {
  description = "The prefix for every resource name."
  type        = string
}

variable "vpc_id" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "instance_security_group_id" {
  description = "The Compose host's security group, the only source admitted on 5432."
  type        = string
}

variable "ssm_path" {
  description = "Where the master password is written for the deploy script's one-time init."
  type        = string
}

variable "kms_key_arn" {
  type = string
}

locals {
  # The master user name; infra/aws/deploy.sh uses the same constant.
  master_username = "dbmaster"
}

resource "aws_security_group" "database" {
  name        = "${var.name}-database"
  description = "PostgreSQL from the Compose host only"
  vpc_id      = var.vpc_id
}

# A separate rule resource (the provider's documented form), so it can name the instance's group, whose own egress names
# this one.
resource "aws_vpc_security_group_ingress_rule" "postgres_from_instance" {
  security_group_id            = aws_security_group.database.id
  description                  = "PostgreSQL from the Compose host"
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  referenced_security_group_id = var.instance_security_group_id
}

resource "aws_db_subnet_group" "this" {
  name       = var.name
  subnet_ids = var.private_subnet_ids
}

resource "aws_db_parameter_group" "this" {
  name   = var.name
  family = "postgres18"

  # Clients must use TLS (Amazon RDS, "Using SSL/TLS with PostgreSQL"); both connect with verify-full.
  parameter {
    name  = "rds.force_ssl"
    value = "1"
  }
}

# Ephemeral, so the value is never stored in state or plan; it reaches RDS and SSM only through write-only arguments
# (Terraform, "Write-only arguments"). Only letters and digits: RDS refuses /, @, " and space in a master password.
ephemeral "random_password" "master" {
  length  = 64
  special = false
}

resource "aws_db_instance" "this" {
  identifier     = var.name
  engine         = "postgres"
  engine_version = "18"
  instance_class = "db.t4g.micro"

  allocated_storage = 20
  storage_type      = "gp3"
  storage_encrypted = true

  db_subnet_group_name   = aws_db_subnet_group.this.name
  parameter_group_name   = aws_db_parameter_group.this.name
  vpc_security_group_ids = [aws_security_group.database.id]
  publicly_accessible    = false
  multi_az               = false

  username = local.master_username
  # The version only changes to set a new password; the value is sent on create and on each version change.
  password_wo         = ephemeral.random_password.master.result
  password_wo_version = 1

  backup_retention_period = 1
  skip_final_snapshot     = true
  deletion_protection     = false
}

# The same ephemeral value, for the deploy script's one-time init; it reads it by name and never puts it in .env.
resource "aws_ssm_parameter" "master_password" {
  name             = "${var.ssm_path}/RDS_MASTER_PASSWORD"
  type             = "SecureString"
  key_id           = var.kms_key_arn
  value_wo         = ephemeral.random_password.master.result
  value_wo_version = 1
}

output "security_group_id" {
  value = aws_security_group.database.id
}

output "address" {
  description = "The endpoint's host name, without the port."
  value       = aws_db_instance.this.address
}
