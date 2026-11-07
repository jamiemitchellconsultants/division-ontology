locals {
  partition = data.aws_partition.current.partition
  account   = data.aws_caller_identity.current.account_id
  ecr_repositories = [
    for image in [var.database_image, var.worker_image] :
    "arn:${local.partition}:ecr:${var.aws_region}:${local.account}:repository/${split("@", trimprefix(image, "${split("/", image)[0]}/"))[0]}"
  ]
}

resource "aws_iam_role" "database" {
  name = "${var.environment}-database"
  assume_role_policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Principal = { Service = "ec2.amazonaws.com" }, Action = "sts:AssumeRole" }]
  })
}
resource "aws_iam_role_policy" "database" {
  role = aws_iam_role.database.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "secretsmanager:GetSecretValue"
        Resource = [var.database_secret_arn, var.api_secret_arn, var.publisher_secret_arn, var.indexer_secret_arn]
      },
      { Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*" },
      {
        Effect   = "Allow"
        Action   = ["ecr:BatchCheckLayerAvailability", "ecr:BatchGetImage", "ecr:GetDownloadUrlForLayer"]
        Resource = local.ecr_repositories
      }
    ]
  })
}
resource "aws_iam_instance_profile" "database" {
  name = "${var.environment}-database"
  role = aws_iam_role.database.name
}
resource "aws_ebs_volume" "database" {
  availability_zone = var.availability_zones[0]
  encrypted         = true
  size              = var.database_volume_gib
  type              = "gp3"
  lifecycle {
    prevent_destroy = true
  }
}
resource "aws_instance" "database" {
  ami                         = var.database_ami
  instance_type               = var.database_instance_type
  subnet_id                   = aws_subnet.private[0].id
  associate_public_ip_address = false
  iam_instance_profile        = aws_iam_instance_profile.database.name
  vpc_security_group_ids      = [aws_security_group.database.id]
  user_data_replace_on_change = true
  user_data = templatefile("${path.module}/bootstrap-db.sh.tftpl", {
    region           = var.aws_region
    volume_id        = aws_ebs_volume.database.id
    database_image   = var.database_image
    worker_image     = var.worker_image
    admin_secret     = var.database_secret_arn
    api_secret       = var.api_secret_arn
    publisher_secret = var.publisher_secret_arn
    indexer_secret   = var.indexer_secret_arn
  })
  root_block_device {
    encrypted = true
  }
  metadata_options {
    http_tokens = "required"
  }
  depends_on = [aws_route_table_association.private, aws_iam_role_policy.database]
}
resource "aws_volume_attachment" "database" {
  device_name                    = "/dev/sdf"
  volume_id                      = aws_ebs_volume.database.id
  instance_id                    = aws_instance.database.id
  stop_instance_before_detaching = true
}
