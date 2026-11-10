resource "aws_vpc" "workspace" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true
}

resource "aws_internet_gateway" "workspace" {
  vpc_id = aws_vpc.workspace.id
}

resource "aws_subnet" "public" {
  count             = 2
  vpc_id            = aws_vpc.workspace.id
  cidr_block        = cidrsubnet(var.vpc_cidr, 4, count.index)
  availability_zone = var.availability_zones[count.index]
}

resource "aws_subnet" "private" {
  count                   = 2
  vpc_id                  = aws_vpc.workspace.id
  cidr_block              = cidrsubnet(var.vpc_cidr, 4, count.index + 2)
  availability_zone       = var.availability_zones[count.index]
  map_public_ip_on_launch = false
}

resource "aws_eip" "nat" {
  domain = "vpc"
}

resource "aws_nat_gateway" "workspace" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.public[0].id
  depends_on    = [aws_internet_gateway.workspace]
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.workspace.id
  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.workspace.id
  }
}
resource "aws_route_table" "private" {
  vpc_id = aws_vpc.workspace.id
  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.workspace.id
  }
}
resource "aws_route_table_association" "public" {
  count          = 2
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}
resource "aws_route_table_association" "private" {
  count          = 2
  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private.id
}

resource "aws_security_group" "database" {
  name   = "${var.environment}-postgres"
  vpc_id = aws_vpc.workspace.id
}
resource "aws_security_group" "workload" {
  for_each = toset(["api", "publisher", "indexer"])
  name     = "${var.environment}-${each.key}"
  vpc_id   = aws_vpc.workspace.id
}

resource "aws_vpc_security_group_ingress_rule" "postgres" {
  for_each                     = aws_security_group.workload
  security_group_id            = aws_security_group.database.id
  referenced_security_group_id = each.value.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}
resource "aws_vpc_security_group_egress_rule" "postgres" {
  for_each                     = aws_security_group.workload
  security_group_id            = each.value.id
  referenced_security_group_id = aws_security_group.database.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}
resource "aws_vpc_security_group_egress_rule" "workload_https" {
  for_each          = aws_security_group.workload
  security_group_id = each.value.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}
resource "aws_vpc_security_group_egress_rule" "database_https" {
  security_group_id = aws_security_group.database.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}
