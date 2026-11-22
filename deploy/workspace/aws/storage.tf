locals {
  bucket_prefix = "${var.environment}-${data.aws_caller_identity.current.account_id}-${var.aws_region}"
  web_files     = fileset(var.web_directory, "**")
  web_types = {
    html  = "text/html; charset=utf-8"
    js    = "text/javascript; charset=utf-8"
    mjs   = "text/javascript; charset=utf-8"
    css   = "text/css; charset=utf-8"
    json  = "application/json"
    wasm  = "application/wasm"
    png   = "image/png"
    svg   = "image/svg+xml"
    woff2 = "font/woff2"
  }
}

resource "aws_s3_bucket" "web" {
  bucket = "${local.bucket_prefix}-web"
}
resource "aws_s3_bucket" "bundles" {
  bucket = "${local.bucket_prefix}-bundles"
  lifecycle {
    prevent_destroy = true
  }
}
resource "aws_s3_bucket_public_access_block" "web" {
  bucket                  = aws_s3_bucket.web.id
  block_public_acls       = true
  ignore_public_acls      = true
  block_public_policy     = true
  restrict_public_buckets = true
}
resource "aws_s3_bucket_public_access_block" "bundles" {
  bucket                  = aws_s3_bucket.bundles.id
  block_public_acls       = true
  ignore_public_acls      = true
  block_public_policy     = true
  restrict_public_buckets = true
}
resource "aws_s3_bucket_versioning" "bundles" {
  bucket = aws_s3_bucket.bundles.id
  versioning_configuration {
    status = "Enabled"
  }
}
resource "aws_s3_object" "web" {
  for_each = toset([
    for file in local.web_files : file
    if file != "appsettings.json" && !endswith(file, ".br") && !endswith(file, ".gz")
  ])
  bucket        = aws_s3_bucket.web.id
  key           = each.value
  source        = "${var.web_directory}/${each.value}"
  source_hash   = filemd5("${var.web_directory}/${each.value}")
  content_type  = lookup(local.web_types, element(reverse(split(".", each.value)), 0), "application/octet-stream")
  cache_control = "no-cache"
}
resource "aws_s3_object" "web_settings" {
  bucket        = aws_s3_bucket.web.id
  key           = "appsettings.json"
  content_type  = "application/json"
  cache_control = "no-store"
  content = jsonencode({
    EvidenceRepositories = var.evidence_repositories
    Identity = {
      Authority = var.entra_issuer
      ClientId  = var.entra_client_id
      ApiScope  = var.entra_api_scope
    }
  })
}
resource "aws_s3_bucket_policy" "web" {
  bucket     = aws_s3_bucket.web.id
  depends_on = [aws_s3_bucket_public_access_block.web]
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "cloudfront.amazonaws.com" }
      Action    = "s3:GetObject"
      Resource  = "${aws_s3_bucket.web.arn}/*"
      Condition = { StringEquals = { "AWS:SourceArn" = aws_cloudfront_distribution.workspace.arn } }
    }]
  })
}
