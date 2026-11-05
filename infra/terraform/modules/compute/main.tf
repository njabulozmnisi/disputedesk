# The one EC2 instance that runs the Compose stack (AD-28): AL2023, IMDSv2 only, no SSH key (SSM Session Manager
# instead), an Elastic IP, and a security group that admits only CloudFront's origin-facing ranges on port 80.

variable "name" {
  description = "The prefix for every resource name."
  type        = string
}

variable "aws_region" {
  type = string
}

variable "account_id" {
  type = string
}

variable "vpc_id" {
  type = string
}

variable "subnet_id" {
  description = "The public subnet."
  type        = string
}

variable "database_security_group_id" {
  description = "The database's security group, the one destination of port 5432."
  type        = string
}

variable "github_repository" {
  description = "<owner>/<name>, cloned on first boot."
  type        = string
}

variable "clone_directory" {
  description = "Where the repository is cloned; the deploy document runs infra/deploy/aws/deploy.sh from it."
  type        = string
}

variable "ssm_path" {
  description = "The deploy script's parameters."
  type        = string
}

variable "dataprotection_ssm_path" {
  description = "The Api's Data Protection key ring."
  type        = string
}

variable "kms_key_arn" {
  description = "The customer-managed key that encrypts the SecureStrings under both paths."
  type        = string
}

locals {
  # The Docker Compose plugin, pinned by version and by the SHA-256 the release publishes for this file
  # (docker-compose-linux-x86_64.sha256), checked before install (Docker, "Install the plugin manually").
  # CI installs the same pin (.github/workflows/ci.yml, COMPOSE_VERSION and COMPOSE_SHA256); change them together.
  compose_version = "5.6.1"
  compose_sha256  = "47737f4236e6d3ccea7291995203f8f018058783b4c3a9469119811a0b5db8c0"

  ssm_parameter_arn = "arn:aws:ssm:${var.aws_region}:${var.account_id}:parameter"
}

# The newest AL2023 x86_64 image (c7i-flex is x86_64).
data "aws_ami" "al2023" {
  owners      = ["amazon"]
  most_recent = true

  filter {
    name   = "name"
    values = ["al2023-ami-2023.*-x86_64"]
  }
}

# CloudFront's origin-facing addresses, as the AWS-managed prefix list (CloudFront, "Locations and IP address ranges").
data "aws_ec2_managed_prefix_list" "cloudfront_origin_facing" {
  name = "com.amazonaws.global.cloudfront.origin-facing"
}

# Every rule inline, so the group's whole rule set is one resource the tests can read: one ingress, two egress. (The
# database group's ingress is a separate rule resource, so the two groups can name each other without a cycle.)
resource "aws_security_group" "instance" {
  name        = "${var.name}-instance"
  description = "The Compose host: port 80 from CloudFront only"
  vpc_id      = var.vpc_id

  ingress {
    description     = "CloudFront origin-facing ranges to the gateway"
    protocol        = "tcp"
    from_port       = 80
    to_port         = 80
    prefix_list_ids = [data.aws_ec2_managed_prefix_list.cloudfront_origin_facing.id]
  }

  # SSM, GHCR, ip-ranges.json, the RDS CA bundle, CloudFront (the containers reach the identity origin through it) and
  # package and release downloads all use HTTPS.
  egress {
    description = "HTTPS to anywhere"
    protocol    = "tcp"
    from_port   = 443
    to_port     = 443
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description     = "PostgreSQL to the database"
    protocol        = "tcp"
    from_port       = 5432
    to_port         = 5432
    security_groups = [var.database_security_group_id]
  }
}

resource "aws_iam_role" "instance" {
  name = "${var.name}-instance"
  # Every IAM policy here is a JSON template whose names the infra tests' IamNamesTests check.
  assume_role_policy = templatefile("${path.module}/trust-policy.json.tftpl", {})
}

# SSM Session Manager and Run Command, and the collector's CloudWatch and X-Ray writes (AD-29). Both are broader than
# least privilege, accepted for the dedicated account (infra/terraform/README.md; the minimal policy is a production-path
# item).
resource "aws_iam_role_policy_attachment" "managed" {
  for_each = toset(["AmazonSSMManagedInstanceCore", "CloudWatchAgentServerPolicy"])

  role       = aws_iam_role.instance.name
  policy_arn = "arn:aws:iam::aws:policy/${each.key}"
}

# Least privilege for what the managed policies do not cover (AWS IAM, "Security best practices"; AWS KMS, "How AWS
# Systems Manager Parameter Store uses AWS KMS"). Both paths are granted as the path itself and everything under it.
# EncryptKeyRing: Parameter Store calls Encrypt with the parameter's ARN as encryption context, so only key-ring
# parameters can be written. XRayOtlpSpans: the X-Ray OTLP endpoint's span upload actions, which
# CloudWatchAgentServerPolicy (v3) does not list (Amazon CloudWatch, "OTLP endpoints", traces endpoint; Service
# Authorization Reference, "Actions, resources, and condition keys for AWS X-Ray": PutSpans, PutSpansForIndexing, no
# resource type).
resource "aws_iam_role_policy" "instance" {
  name = "${var.name}-instance"
  role = aws_iam_role.instance.id
  policy = templatefile("${path.module}/instance-policy.json.tftpl", {
    ssm_parameter_arn       = local.ssm_parameter_arn
    ssm_path                = var.ssm_path
    dataprotection_ssm_path = var.dataprotection_ssm_path
    kms_key_arn             = var.kms_key_arn
    aws_region              = var.aws_region
  })
}

resource "aws_iam_instance_profile" "instance" {
  name = "${var.name}-instance"
  role = aws_iam_role.instance.name
}

resource "aws_instance" "this" {
  ami                         = data.aws_ami.al2023.id
  instance_type               = "c7i-flex.large"
  subnet_id                   = var.subnet_id
  vpc_security_group_ids      = [aws_security_group.instance.id]
  iam_instance_profile        = aws_iam_instance_profile.instance.name
  associate_public_ip_address = true # the first boot reaches the internet without waiting for the Elastic IP

  # IMDSv2 only; hop limit 2 so the Api and the collector, one bridge hop away, reach it (the deploy script's
  # DOCKER-USER rule keeps every other container out).
  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 2
  }

  # The images, the swap file and the Docker volumes outgrow AL2023's 8 GiB default.
  root_block_device {
    volume_size = 30
    volume_type = "gp3"
    encrypted   = true
  }

  user_data = templatefile("${path.module}/user-data.sh.tftpl", {
    compose_version   = local.compose_version
    compose_sha256    = local.compose_sha256
    github_repository = var.github_repository
    clone_directory   = var.clone_directory
  })

  # A newer AL2023 image must not replace the instance (and its Docker volumes) on the next apply.
  lifecycle {
    ignore_changes = [ami]
  }

  tags = { Name = var.name }
}

resource "aws_eip" "this" {
  domain   = "vpc"
  instance = aws_instance.this.id

  tags = { Name = var.name }
}

output "instance_id" {
  value = aws_instance.this.id
}

output "instance_arn" {
  value = aws_instance.this.arn
}

output "security_group_id" {
  value = aws_security_group.instance.id
}

# The Elastic IP's public DNS name, CloudFront's origin (not the instance's name from before the Elastic IP).
output "public_dns" {
  value = aws_eip.this.public_dns
}
