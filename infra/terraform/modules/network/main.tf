# One VPC: a public subnet for the instance, with an internet gateway route (no NAT gateway), and two private subnets
# in two availability zones, which an RDS subnet group requires. DNS support and host names are on, so the Elastic IP
# has a public DNS name for CloudFront's origin (Amazon VPC, "DNS attributes for your VPC").

variable "name" {
  description = "The prefix for every resource name."
  type        = string
}

data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  # Two zones for the RDS subnet group; the instance's subnet is in the second.
  zones = slice(data.aws_availability_zones.available.names, 0, 2)
}

# 10.20.0.0/16 keeps clear of the Compose subnet (10.201.89.0/24) and Docker's default bridge (172.17.0.0/16).
resource "aws_vpc" "this" {
  cidr_block           = "10.20.0.0/16"
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = { Name = var.name }
}

resource "aws_internet_gateway" "this" {
  vpc_id = aws_vpc.this.id

  tags = { Name = var.name }
}

# The instance sits in this subnet, in the second zone: in af-south-1 the first zone has no c7i-flex.large capacity
# (InsufficientInstanceCapacity).
resource "aws_subnet" "public" {
  vpc_id            = aws_vpc.this.id
  cidr_block        = "10.20.0.0/24"
  availability_zone = local.zones[1]

  tags = { Name = "${var.name}-public" }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.this.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.this.id
  }

  tags = { Name = "${var.name}-public" }
}

resource "aws_route_table_association" "public" {
  subnet_id      = aws_subnet.public.id
  route_table_id = aws_route_table.public.id
}

# No route to the internet: the database is reachable only from inside the VPC.
resource "aws_subnet" "private" {
  count = 2

  vpc_id            = aws_vpc.this.id
  cidr_block        = "10.20.${10 + count.index}.0/24"
  availability_zone = local.zones[count.index]

  tags = { Name = "${var.name}-private-${count.index}" }
}

output "vpc_id" {
  value = aws_vpc.this.id
}

output "public_subnet_id" {
  value = aws_subnet.public.id

  # Whatever uses the subnet waits for its internet route: the instance's first boot downloads packages, and an Elastic
  # IP needs the internet gateway before it can be associated (Terraform AWS provider, aws_eip).
  depends_on = [aws_route_table_association.public]
}

output "private_subnet_ids" {
  value = aws_subnet.private[*].id
}
