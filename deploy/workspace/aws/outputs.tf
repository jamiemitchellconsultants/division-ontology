output "workspace_url" {
  value = "https://${aws_cloudfront_distribution.workspace.domain_name}"
}
output "entra_redirect_uri" {
  value = "https://${aws_cloudfront_distribution.workspace.domain_name}/authentication/login-callback"
}
output "entra_docs_redirect_uri" {
  value = "https://${aws_cloudfront_distribution.workspace.domain_name}/api/docs/oauth2-redirect.html"
}
output "bundle_bucket" {
  value = aws_s3_bucket.bundles.bucket
}
output "database_connection_limit" {
  value = local.connections
}
