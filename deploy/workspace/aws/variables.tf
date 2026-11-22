variable "environment" {
  type = string
  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,26}[a-z0-9]$", var.environment))
    error_message = "Use a short lowercase environment name."
  }
}
variable "aws_region" {
  type = string
}
variable "availability_zones" {
  type = list(string)
  validation {
    condition     = length(var.availability_zones) == 2 && length(toset(var.availability_zones)) == 2
    error_message = "Provide two distinct availability zones."
  }
}
variable "vpc_cidr" {
  type    = string
  default = "10.72.0.0/16"
}
variable "entra_issuer" {
  type = string
  validation {
    condition     = startswith(var.entra_issuer, "https://") && endswith(var.entra_issuer, "/v2.0")
    error_message = "Supply the exact tenant-specific Entra v2 HTTPS issuer."
  }
}
variable "entra_audience" {
  type = string
}
variable "entra_client_id" {
  type = string
}
variable "entra_api_scope" {
  type = string
}
variable "database_ami" {
  type        = string
  description = "Approved Amazon Linux 2023 x86_64 AMI."
}
variable "database_image" {
  type        = string
  description = "PostgreSQL workspace image in regional private ECR, pinned by digest."
  validation {
    condition     = can(regex("^[0-9]{12}\\.dkr\\.ecr\\.[a-z0-9-]+\\.amazonaws\\.com/[a-z0-9/_-]+@sha256:[a-f0-9]{64}$", var.database_image))
    error_message = "Supply a private ECR database image pinned by SHA-256."
  }
}
variable "worker_image" {
  type        = string
  description = "Workspace worker image in regional private ECR, pinned by digest, for controlled migrations and larger jobs."
  validation {
    condition     = can(regex("^[0-9]{12}\\.dkr\\.ecr\\.[a-z0-9-]+\\.amazonaws\\.com/[a-z0-9/_-]+@sha256:[a-f0-9]{64}$", var.worker_image))
    error_message = "Supply a private ECR worker image pinned by SHA-256."
  }
}
variable "database_instance_type" {
  type    = string
  default = "t3.medium"
}
variable "database_volume_gib" {
  type    = number
  default = 32
}
variable "database_connection_budget" {
  type    = number
  default = 24
}
variable "database_secret_arn" {
  type        = string
  description = "ARN of the PostgreSQL administrator secret."
}
variable "api_secret_arn" {
  type = string
}
variable "publisher_secret_arn" {
  type = string
}
variable "indexer_secret_arn" {
  type = string
}
variable "artifact_bucket" {
  type        = string
  description = "Existing private regional S3 bucket containing immutable release ZIP files."
}
variable "api_package_key" {
  type = string
}
variable "worker_package_key" {
  type = string
}
variable "web_directory" {
  type        = string
  description = "Directory containing the published Blazor wwwroot."
}
variable "pending_manifest" {
  type        = string
  default     = null
  description = "Pinned digest to stage and activate through the publisher schedule."
  validation {
    condition     = var.pending_manifest == null ? true : can(regex("^[a-f0-9]{64}$", var.pending_manifest))
    error_message = "The manifest ID must be a content SHA-256 digest."
  }
}

provider "aws" {
  region = var.aws_region
  default_tags {
    tags = { Application = var.environment }
  }
}
data "aws_caller_identity" "current" {}
data "aws_partition" "current" {}

variable "evidence_repositories" {
  type    = map(string)
  default = {}
  validation {
    condition = alltrue([
      for repo in values(var.evidence_repositories) :
      can(regex("^https://github\\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", repo))
    ])
    error_message = "Evidence repositories must use HTTPS GitHub owner/repository URLs."
  }
}
