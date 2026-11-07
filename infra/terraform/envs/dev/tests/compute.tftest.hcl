# Story 7.5, criterion 2, and the instance role of criterion 7: plans of the network and compute modules against a
# mocked AWS provider, so no run reaches AWS or needs credentials (Terraform, "Tests" and "Mocks"). Each run plans one
# module (a run's module block), because assertions read only the root module's resources. override_during = plan gives
# every computed attribute an assertion reads a known value in the plan.
#   terraform init -backend=false && terraform test

mock_provider "aws" {
  override_during = plan

  mock_data "aws_availability_zones" {
    defaults = { names = ["eu-west-1a", "eu-west-1b", "eu-west-1c"] }
  }

  mock_data "aws_ami" {
    defaults = { id = "ami-0000-0000-000000001" }
  }

  mock_data "aws_ec2_managed_prefix_list" {
    defaults = { id = "pl-00000001" }
  }

  # No key pair: the provider would otherwise give the computed key_name a random mock value.
  mock_resource "aws_instance" {
    defaults = { id = "i-0000-0000-000000001", key_name = "" }
  }

  mock_resource "aws_internet_gateway" {
    defaults = { id = "igw-0000-0000-000000001" }
  }

  mock_resource "aws_subnet" {
    defaults = { id = "subnet-0000-0000-000000001" }
  }
}

variables {
  name                       = "disputedesk-dev"
  aws_region                 = "eu-west-1"
  account_id                 = "0000-0000-0000"
  vpc_id                     = "vpc-0000-0000-000000001"
  subnet_id                  = "subnet-0000-0000-000000001"
  database_security_group_id = "sg-0000-0000-000000002"
  github_repository          = "example-owner/disputedesk"
  clone_directory            = "/opt/disputedesk"
  ssm_path                   = "/disputedesk/dev"
  dataprotection_ssm_path    = "/disputedesk-dataprotection/dev"
  kms_key_arn                = "arn:aws:kms:eu-west-1:0000-0000-0000:key/00000000-0000-0000-0000-00000000000a"
}

run "network_has_a_public_subnet_with_an_internet_gateway_and_no_nat" {
  command = plan

  module {
    source = "../../modules/network"
  }

  assert {
    condition     = aws_vpc.this.enable_dns_support == true && aws_vpc.this.enable_dns_hostnames == true
    error_message = "The VPC does not have DNS support and DNS host names (the Elastic IP needs a public DNS name)."
  }

  assert {
    condition = (
      one(aws_route_table.public.route).cidr_block == "0.0.0.0/0" &&
      one(aws_route_table.public.route).gateway_id == "igw-0000-0000-000000001" &&
      one(aws_route_table.public.route).nat_gateway_id == null &&
      aws_route_table_association.public.subnet_id == aws_subnet.public.id
    )
    error_message = "The public subnet's only route is not 0.0.0.0/0 to the internet gateway."
  }

  assert {
    condition     = length(aws_subnet.private) == 2 && aws_subnet.private[0].availability_zone != aws_subnet.private[1].availability_zone
    error_message = "There are not two private subnets in two zones for the RDS subnet group."
  }

  assert {
    condition     = aws_subnet.public.availability_zone == "eu-west-1b"
    error_message = "The public subnet, and so the instance, is not in the second available zone."
  }
}

run "compute_is_one_imdsv2_instance_open_only_to_cloudfront_on_port_80" {
  command = plan

  module {
    source = "../../modules/compute"
  }

  assert {
    condition = (
      aws_instance.this.instance_type == "c7i-flex.large" &&
      aws_instance.this.ami == "ami-0000-0000-000000001" &&
      aws_instance.this.key_name == "" &&
      aws_instance.this.associate_public_ip_address == true &&
      aws_instance.this.subnet_id == "subnet-0000-0000-000000001"
    )
    error_message = "The instance is not a c7i-flex.large from the AL2023 image, without a key pair, with a public address, in the public subnet."
  }

  assert {
    condition     = toset(data.aws_ami.al2023.owners) == toset(["amazon"]) && toset(one(data.aws_ami.al2023.filter).values) == toset(["al2023-ami-2023.*-x86_64"])
    error_message = "The image is not Amazon's AL2023 x86_64."
  }

  assert {
    condition = (
      aws_instance.this.metadata_options[0].http_tokens == "required" &&
      aws_instance.this.metadata_options[0].http_put_response_hop_limit == 2
    )
    error_message = "The instance does not require IMDSv2 with hop limit 2."
  }

  assert {
    condition = (
      aws_instance.this.iam_instance_profile == aws_iam_instance_profile.instance.name &&
      aws_iam_instance_profile.instance.role == aws_iam_role.instance.name &&
      aws_iam_role_policy_attachment.managed["AmazonSSMManagedInstanceCore"].policy_arn == "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
    )
    error_message = "The instance profile does not carry AmazonSSMManagedInstanceCore."
  }

  assert {
    condition     = aws_eip.this.instance == "i-0000-0000-000000001" && aws_eip.this.domain == "vpc"
    error_message = "The instance has no Elastic IP."
  }

  assert {
    condition = (
      length(aws_security_group.instance.ingress) == 1 &&
      one(aws_security_group.instance.ingress).protocol == "tcp" &&
      one(aws_security_group.instance.ingress).from_port == 80 &&
      one(aws_security_group.instance.ingress).to_port == 80 &&
      toset(one(aws_security_group.instance.ingress).prefix_list_ids) == toset(["pl-00000001"]) &&
      one(aws_security_group.instance.ingress).cidr_blocks == null &&
      one(aws_security_group.instance.ingress).ipv6_cidr_blocks == null &&
      one(aws_security_group.instance.ingress).security_groups == null
    )
    error_message = "The instance's only ingress is not TCP 80 from the CloudFront origin-facing prefix list (no 22, no 0.0.0.0/0)."
  }

  assert {
    condition     = data.aws_ec2_managed_prefix_list.cloudfront_origin_facing.name == "com.amazonaws.global.cloudfront.origin-facing"
    error_message = "The prefix list is not CloudFront's origin-facing list."
  }

  assert {
    condition = (
      length(aws_security_group.instance.egress) == 2 &&
      length([for rule in aws_security_group.instance.egress : rule if
        rule.protocol == "tcp" && rule.from_port == 443 && rule.to_port == 443 && toset(rule.cidr_blocks) == toset(["0.0.0.0/0"]) &&
      rule.security_groups == null]) == 1 &&
      length([for rule in aws_security_group.instance.egress : rule if
        rule.protocol == "tcp" && rule.from_port == 5432 && rule.to_port == 5432 && rule.cidr_blocks == null &&
      toset(rule.security_groups) == toset(["sg-0000-0000-000000002"])]) == 1
    )
    error_message = "The instance's egress is not exactly TCP 443 to anywhere and TCP 5432 to the database's security group."
  }

  assert {
    condition = (
      strcontains(aws_instance.this.user_data, "releases/download/v5.6.1/docker-compose-linux-x86_64") &&
      strcontains(aws_instance.this.user_data, "echo \"47737f4236e6d3ccea7291995203f8f018058783b4c3a9469119811a0b5db8c0  /tmp/docker-compose\" | sha256sum --check --strict")
    )
    error_message = "The user data does not install the Compose plugin v5.6.1 checked against its SHA-256."
  }

  assert {
    condition = (
      strcontains(aws_instance.this.user_data, "dnf install -y docker git jq iptables-services") &&
      strcontains(aws_instance.this.user_data, "dd if=/dev/zero of=/swapfile bs=1M count=2048") &&
      strcontains(aws_instance.this.user_data, "swapon /swapfile") &&
      strcontains(aws_instance.this.user_data, "git clone --quiet \"https://github.com/example-owner/disputedesk.git\" \"/opt/disputedesk\"")
    )
    error_message = "The user data does not install Docker, git, jq and iptables-services, add 2 GiB of swap and clone the repository."
  }

  assert {
    condition     = !strcontains(aws_instance.this.user_data, "postgresql")
    error_message = "The user data installs psql; the init runs it from the pinned postgres image."
  }
}

run "criterion_7_the_instance_role_is_least_privilege_on_the_two_paths_and_the_one_key" {
  command = plan

  module {
    source = "../../modules/compute"
  }

  assert {
    condition     = keys(aws_iam_role_policy_attachment.managed) == ["AmazonSSMManagedInstanceCore", "CloudWatchAgentServerPolicy"]
    error_message = "The instance role's managed policies are not exactly AmazonSSMManagedInstanceCore and CloudWatchAgentServerPolicy."
  }

  assert {
    condition = jsondecode(aws_iam_role_policy.instance.policy).Statement == [
      {
        Sid      = "DeployParameters"
        Effect   = "Allow"
        Action   = "ssm:GetParametersByPath"
        Resource = ["arn:aws:ssm:eu-west-1:0000-0000-0000:parameter/disputedesk/dev", "arn:aws:ssm:eu-west-1:0000-0000-0000:parameter/disputedesk/dev/*"]
      },
      {
        Sid      = "DataProtectionKeyRing"
        Effect   = "Allow"
        Action   = ["ssm:GetParametersByPath", "ssm:PutParameter"]
        Resource = ["arn:aws:ssm:eu-west-1:0000-0000-0000:parameter/disputedesk-dataprotection/dev", "arn:aws:ssm:eu-west-1:0000-0000-0000:parameter/disputedesk-dataprotection/dev/*"]
      },
      {
        Sid       = "DecryptThroughSsm"
        Effect    = "Allow"
        Action    = "kms:Decrypt"
        Resource  = "arn:aws:kms:eu-west-1:0000-0000-0000:key/00000000-0000-0000-0000-00000000000a"
        Condition = { StringEquals = { "kms:ViaService" = "ssm.eu-west-1.amazonaws.com" } }
      },
      {
        Sid       = "EncryptKeyRing"
        Effect    = "Allow"
        Action    = "kms:Encrypt"
        Resource  = "arn:aws:kms:eu-west-1:0000-0000-0000:key/00000000-0000-0000-0000-00000000000a"
        Condition = { StringLike = { "kms:EncryptionContext:PARAMETER_ARN" = "arn:aws:ssm:eu-west-1:0000-0000-0000:parameter/disputedesk-dataprotection/dev/*" } }
      },
      {
        Sid      = "XRayOtlpSpans"
        Effect   = "Allow"
        Action   = ["xray:PutSpans", "xray:PutSpansForIndexing"]
        Resource = "*"
      },
    ]
    error_message = "The instance role's own policy is not least privilege on the two paths and the one key."
  }
}

# Each of these values reaches a template, where a quote or a backslash would change the rendered document.
run "template_inputs_refuse_a_quote_or_a_backslash" {
  command = plan

  module {
    source = "../../modules/compute"
  }

  variables {
    aws_region              = "eu-west-1\""
    account_id              = "0000\\0000"
    github_repository       = "example-owner/disputedesk\""
    clone_directory         = "/opt\\disputedesk"
    ssm_path                = "/x\", \"*"
    dataprotection_ssm_path = "/x\\y"
    kms_key_arn             = "arn:aws:kms:*\""
  }

  expect_failures = [
    var.aws_region,
    var.account_id,
    var.github_repository,
    var.clone_directory,
    var.ssm_path,
    var.dataprotection_ssm_path,
    var.kms_key_arn,
  ]
}
