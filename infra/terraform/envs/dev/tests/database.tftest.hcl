# Story 7.5, criteria 1 (the write-only master password) and 3: a plan of the database module against a mocked AWS
# provider. The random provider runs for real (it works offline; mocks do not support the ephemeral master password).
#   terraform init -backend=false && terraform test

mock_provider "aws" {
  override_during = plan

  mock_resource "aws_security_group" {
    defaults = { id = "sg-0000-0000-000000002" }
  }
}

variables {
  name                       = "disputedesk-dev"
  vpc_id                     = "vpc-0000-0000-000000001"
  private_subnet_ids         = ["subnet-0000-0000-000000010", "subnet-0000-0000-000000011"]
  instance_security_group_id = "sg-0000-0000-000000001"
  ssm_path                   = "/disputedesk/dev"
  kms_key_arn                = "arn:aws:kms:eu-west-1:0000-0000-0000:key/00000000-0000-0000-0000-00000000000a"
}

run "criterion_1_the_master_password_reaches_rds_and_ssm_only_through_write_only_arguments" {
  command = plan

  module {
    source = "../../modules/database"
  }

  assert {
    condition     = aws_db_instance.this.password == null && aws_db_instance.this.password_wo_version == 1
    error_message = "The RDS master password is not set through password_wo."
  }

  assert {
    condition = [
      aws_ssm_parameter.master_password.name,
      aws_ssm_parameter.master_password.type,
      aws_ssm_parameter.master_password.key_id,
      tostring(aws_ssm_parameter.master_password.value_wo_version),
      ] == [
      "/disputedesk/dev/RDS_MASTER_PASSWORD",
      "SecureString",
      "arn:aws:kms:eu-west-1:0000-0000-0000:key/00000000-0000-0000-0000-00000000000a",
      "1",
    ]
    error_message = "RDS_MASTER_PASSWORD is not a write-only SecureString under ssm_path and the customer-managed key."
  }
}

run "criterion_3_the_database_is_private_and_tears_down_cleanly" {
  command = plan

  module {
    source = "../../modules/database"
  }

  assert {
    condition = (
      aws_db_instance.this.engine == "postgres" &&
      aws_db_instance.this.engine_version == "18" &&
      aws_db_parameter_group.this.family == "postgres18" &&
      aws_db_instance.this.parameter_group_name == aws_db_parameter_group.this.name &&
      aws_db_instance.this.instance_class == "db.t4g.micro" &&
      aws_db_instance.this.multi_az == false &&
      aws_db_instance.this.username == "dbmaster"
    )
    error_message = "The database is not single-AZ PostgreSQL 18 on db.t4g.micro with master user dbmaster."
  }

  assert {
    condition = (
      aws_db_instance.this.publicly_accessible == false &&
      aws_db_instance.this.storage_encrypted == true &&
      aws_db_instance.this.db_subnet_group_name == aws_db_subnet_group.this.name &&
      toset(aws_db_subnet_group.this.subnet_ids) == toset(["subnet-0000-0000-000000010", "subnet-0000-0000-000000011"])
    )
    error_message = "The database is public, unencrypted or outside the private subnets."
  }

  assert {
    condition = (
      toset(aws_db_instance.this.vpc_security_group_ids) == toset(["sg-0000-0000-000000002"]) &&
      aws_vpc_security_group_ingress_rule.postgres_from_instance.security_group_id == "sg-0000-0000-000000002" &&
      aws_vpc_security_group_ingress_rule.postgres_from_instance.referenced_security_group_id == "sg-0000-0000-000000001" &&
      aws_vpc_security_group_ingress_rule.postgres_from_instance.ip_protocol == "tcp" &&
      aws_vpc_security_group_ingress_rule.postgres_from_instance.from_port == 5432 &&
      aws_vpc_security_group_ingress_rule.postgres_from_instance.to_port == 5432 &&
      aws_vpc_security_group_ingress_rule.postgres_from_instance.cidr_ipv4 == null &&
      length(aws_security_group.database.ingress) == 0
    )
    error_message = "The database admits something other than 5432 from the instance's security group."
  }

  assert {
    condition = (
      one(aws_db_parameter_group.this.parameter).name == "rds.force_ssl" &&
      one(aws_db_parameter_group.this.parameter).value == "1"
    )
    error_message = "The parameter group does not set rds.force_ssl = 1."
  }

  assert {
    condition = (
      aws_db_instance.this.backup_retention_period == 1 &&
      aws_db_instance.this.skip_final_snapshot == true &&
      aws_db_instance.this.deletion_protection == false
    )
    error_message = "The database keeps more than one day of backups, takes a final snapshot or is protected from deletion."
  }
}
