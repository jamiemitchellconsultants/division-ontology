mock_provider "aws" {
  mock_data "aws_caller_identity" {
    defaults = { account_id = "123456789012" }
  }
  mock_data "aws_partition" {
    defaults = { partition = "aws", dns_suffix = "amazonaws.com" }
  }
}

variables {
  environment          = "workspace-test"
  aws_region           = "eu-west-2"
  availability_zones   = ["eu-west-2a", "eu-west-2b"]
  entra_issuer         = "https://login.example.test/tenant/v2.0"
  entra_audience       = "api://fixture"
  entra_client_id      = "fixture-client"
  entra_api_scope      = "api://fixture/workspace"
  database_ami         = "ami-0123456789abcdef0"
  database_image       = "123456789012.dkr.ecr.eu-west-2.amazonaws.com/workspace-db@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  worker_image         = "123456789012.dkr.ecr.eu-west-2.amazonaws.com/workspace-worker@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  database_secret_arn  = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:fixture-admin"
  api_secret_arn       = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:fixture-api"
  publisher_secret_arn = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:fixture-publisher"
  indexer_secret_arn   = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:fixture-indexer"
  artifact_bucket      = "fixture-code-artifacts"
  api_package_key      = "releases/fixture/api.zip"
  worker_package_key   = "releases/fixture/worker.zip"
  web_directory        = "tests/fixture"
}

run "private_database_and_serverless_api" {
  command = plan

  assert {
    condition     = aws_s3_object.web["graph-diagram.mjs"].content_type == "text/javascript; charset=utf-8"
    error_message = "Browser modules must be served as JavaScript."
  }
  assert {
    condition     = aws_ebs_volume.database.encrypted && !aws_instance.database.associate_public_ip_address
    error_message = "Workspace PostgreSQL needs encrypted durable storage on a private host."
  }
  assert {
    condition = strcontains(templatefile("${path.module}/bootstrap-db.sh.tftpl", {
      region           = var.aws_region
      volume_id        = "vol-0123456789abcdef0"
      database_image   = var.database_image
      worker_image     = var.worker_image
      admin_secret     = var.database_secret_arn
      api_secret       = var.api_secret_arn
      publisher_secret = var.publisher_secret_arn
      indexer_secret   = var.indexer_secret_arn
    }), "PGDATA=/var/lib/postgresql/data/workspace")
    error_message = "PostgreSQL must initialize in a child of the EBS filesystem root."
  }
  assert {
    condition     = aws_lambda_function.workload["api"].reserved_concurrent_executions > 0 && local.connections <= var.database_connection_budget
    error_message = "Each function needs bounded concurrency and the total must fit the connection budget."
  }
  assert {
    condition     = local.workloads.api.environment.Workspace__Docs__ClientId == var.entra_client_id && local.workloads.api.environment.Workspace__Docs__Scopes == var.entra_api_scope
    error_message = "The docs UI must reuse the web client and API scope."
  }
  assert {
    condition     = aws_s3_bucket_public_access_block.bundles.block_public_policy && aws_s3_bucket_public_access_block.web.block_public_policy
    error_message = "Web and bundle buckets need private S3 access."
  }
  assert {
    condition     = local.workloads.api.environment.Workspace__Identity__Provider == "Entra" && local.workloads.indexer.environment.Workspace__Embeddings__Model == "amazon.titan-embed-text-v2:0"
    error_message = "AWS needs Entra authentication and the Titan V2 embedding profile."
  }
  assert {
    condition     = aws_vpc_security_group_ingress_rule.postgres["api"].from_port == 5432 && length(aws_vpc_security_group_ingress_rule.postgres) == 3
    error_message = "Only the three application workload groups may connect to PostgreSQL."
  }
  assert {
    condition     = !contains(keys(local.workloads.api.environment), "Workspace__Objects__Bucket") && !contains(keys(local.workloads.indexer.environment), "Workspace__Objects__Bucket") && contains(keys(local.workloads.publisher.environment), "Workspace__Objects__Bucket")
    error_message = "Only the publisher uses the immutable bundle store."
  }
  assert {
    condition     = aws_cloudfront_distribution.workspace.ordered_cache_behavior[0].path_pattern == "/api/*" && aws_cloudfront_distribution.workspace.ordered_cache_behavior[0].target_origin_id == "api"
    error_message = "Authenticated API routes must keep their own CloudFront behavior."
  }
  assert {
    condition     = aws_cloudwatch_event_rule.worker["publisher"].state == "DISABLED" && aws_cloudwatch_event_rule.worker["indexer"].state == "ENABLED"
    error_message = "The publisher should remain idle until a reviewed manifest is supplied."
  }
  assert {
    condition     = !strcontains(aws_iam_role_policy.workload["api"].policy, "s3:PutObject") && !strcontains(aws_iam_role_policy.workload["publisher"].policy, "bedrock:InvokeModel")
    error_message = "AWS workload policies must stay separated."
  }
}
