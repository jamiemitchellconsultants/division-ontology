locals {
  titan = "amazon.titan-embed-text-v2:0"
  bedrock = {
    Workspace__Embeddings__Provider   = "Bedrock"
    Workspace__Embeddings__Region     = var.aws_region
    Workspace__Embeddings__Model      = local.titan
    Workspace__Embeddings__Revision   = "v2:0"
    Workspace__Embeddings__Dimensions = "1024"
  }
  workloads = {
    api = {
      key         = var.api_package_key
      handler     = "RuntimeKnowledge.Api::RuntimeKnowledge.Api.LambdaHosting::FunctionHandlerAsync"
      concurrency = 4
      pool        = 3
      timeout     = 29
      secret      = var.api_secret_arn
      environment = merge(local.api_identity, local.bedrock)
    }
    indexer = {
      key         = var.worker_package_key
      handler     = "RuntimeKnowledge.Worker::RuntimeKnowledge.Worker.LambdaEntry::IndexHandler"
      concurrency = 1
      pool        = 2
      timeout     = 120
      secret      = var.indexer_secret_arn
      environment = local.bedrock
    }
    publisher = {
      key         = var.worker_package_key
      handler     = "RuntimeKnowledge.Worker::RuntimeKnowledge.Worker.LambdaEntry::ImportHandler"
      concurrency = 1
      pool        = 2
      timeout     = 180
      secret      = var.publisher_secret_arn
      environment = {
        Workspace__Objects__Bucket = aws_s3_bucket.bundles.bucket
        Workspace__Objects__Region = var.aws_region
      }
    }
  }
  connections = sum([for workload in local.workloads : workload.concurrency * workload.pool])
  bundle_arn  = "arn:${local.partition}:s3:::${local.bucket_prefix}-bundles"
}

resource "aws_cloudwatch_log_group" "workload" {
  for_each          = local.workloads
  name              = "/aws/lambda/${var.environment}-${each.key}"
  retention_in_days = 30
}

resource "aws_iam_role" "workload" {
  for_each = local.workloads
  name     = "${var.environment}-${each.key}"
  assume_role_policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Principal = { Service = "lambda.amazonaws.com" }, Action = "sts:AssumeRole" }]
  })
}
resource "aws_iam_role_policy" "workload" {
  for_each = local.workloads
  role     = aws_iam_role.workload[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat([
      {
        Effect   = "Allow"
        Action   = ["logs:CreateLogStream", "logs:PutLogEvents"]
        Resource = "arn:${local.partition}:logs:${var.aws_region}:${local.account}:log-group:/aws/lambda/${var.environment}-${each.key}:*"
      },
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = [each.value.secret]
      },
      {
        Effect = "Allow"
        Action = [
          "ec2:CreateNetworkInterface", "ec2:DescribeNetworkInterfaces", "ec2:DescribeSubnets",
          "ec2:DeleteNetworkInterface", "ec2:AssignPrivateIpAddresses", "ec2:UnassignPrivateIpAddresses"
        ]
        Resource = "*"
      }
      ], each.key == "publisher" ? [{
        Effect   = "Allow"
        Action   = ["s3:GetObject"]
        Resource = ["${local.bundle_arn}/blobs/*", "${local.bundle_arn}/manifests/*"]
        }] : [{
        Effect   = "Allow"
        Action   = ["bedrock:InvokeModel"]
        Resource = ["arn:${local.partition}:bedrock:${var.aws_region}::foundation-model/${local.titan}"]
    }])
  })
}
resource "aws_lambda_function" "workload" {
  for_each                       = local.workloads
  function_name                  = "${var.environment}-${each.key}"
  role                           = aws_iam_role.workload[each.key].arn
  runtime                        = "dotnet10"
  handler                        = each.value.handler
  architectures                  = ["x86_64"]
  s3_bucket                      = var.artifact_bucket
  s3_key                         = each.value.key
  timeout                        = each.value.timeout
  memory_size                    = 1024
  reserved_concurrent_executions = each.value.concurrency

  environment {
    variables = merge(each.value.environment, {
      Workspace__Database__SecretArn       = each.value.secret
      Workspace__Database__Host            = aws_instance.database.private_ip
      Workspace__Database__MaximumPoolSize = tostring(each.value.pool)
    })
  }
  vpc_config {
    subnet_ids         = aws_subnet.private[*].id
    security_group_ids = [aws_security_group.workload[each.key].id]
  }
  lifecycle {
    precondition {
      condition     = local.connections <= var.database_connection_budget
      error_message = "The combined Lambda concurrency and pools exceed the database connection budget."
    }
  }
  depends_on = [aws_iam_role_policy.workload, aws_cloudwatch_log_group.workload, aws_volume_attachment.database]
}

resource "aws_cloudwatch_event_rule" "worker" {
  for_each            = toset(["publisher", "indexer"])
  name                = "${var.environment}-${each.key}"
  schedule_expression = "rate(1 minute)"
  state               = each.key == "publisher" && var.pending_manifest == null ? "DISABLED" : "ENABLED"
}
resource "aws_cloudwatch_event_target" "worker" {
  for_each = aws_cloudwatch_event_rule.worker
  rule     = each.value.name
  arn      = aws_lambda_function.workload[each.key].arn
  input    = each.key == "publisher" ? jsonencode({ manifestId = var.pending_manifest }) : "{}"
  retry_policy {
    maximum_event_age_in_seconds = 60
    maximum_retry_attempts       = 0
  }
}
resource "aws_lambda_permission" "scheduled" {
  for_each      = aws_cloudwatch_event_rule.worker
  statement_id  = "AllowWorkspaceSchedule"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.workload[each.key].function_name
  principal     = "events.amazonaws.com"
  source_arn    = each.value.arn
}

resource "aws_apigatewayv2_api" "workspace" {
  name          = var.environment
  protocol_type = "HTTP"
}
resource "aws_apigatewayv2_integration" "api" {
  api_id                 = aws_apigatewayv2_api.workspace.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.workload["api"].invoke_arn
  payload_format_version = "2.0"
}
resource "aws_apigatewayv2_route" "api" {
  api_id    = aws_apigatewayv2_api.workspace.id
  route_key = "ANY /api/{proxy+}"
  target    = "integrations/${aws_apigatewayv2_integration.api.id}"
}
resource "aws_apigatewayv2_stage" "default" {
  api_id      = aws_apigatewayv2_api.workspace.id
  name        = "$default"
  auto_deploy = true
}
resource "aws_lambda_permission" "api_gateway" {
  statement_id  = "AllowWorkspaceGateway"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.workload["api"].function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.workspace.execution_arn}/*/*/api/*"
}
