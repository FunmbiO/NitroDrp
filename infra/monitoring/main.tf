# Uptime alerting and security logging for the Ready Set Build account.
# Separate root module so it never touches the app stack in ../terraform.
#
#   terraform init -backend-config=../terraform/backend.hcl -backend-config="key=nitrodrp/monitoring.tfstate"
#   terraform apply

terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
  backend "s3" {}
}

provider "aws" {
  region              = var.region
  allowed_account_ids = [var.aws_account_id]
}

# Route 53 health-check metrics only exist in us-east-1, so the alarm and its topic live there.
provider "aws" {
  alias               = "use1"
  region              = "us-east-1"
  allowed_account_ids = [var.aws_account_id]
}

data "aws_caller_identity" "current" {}
data "aws_partition" "current" {}

# --- Is the site up? ----------------------------------------------------------------------------
# Checks from several places outside AWS's network path to your server, every 30 seconds.

resource "aws_sns_topic" "alerts" {
  provider = aws.use1
  name     = "nitrodrp-alerts"
}

resource "aws_sns_topic_subscription" "email" {
  provider  = aws.use1
  topic_arn = aws_sns_topic.alerts.arn
  protocol  = "email"
  endpoint  = var.alert_email # AWS emails a confirmation link: click it or nothing arrives.
}

resource "aws_route53_health_check" "site" {
  provider          = aws.use1
  fqdn              = var.site_host
  port              = 443
  type              = "HTTPS"
  resource_path     = "/"
  request_interval  = 30
  failure_threshold = 3
  enable_sni        = true
  tags              = { Name = "nitrodrp-site" }
}

resource "aws_cloudwatch_metric_alarm" "site_down" {
  provider            = aws.use1
  alarm_name          = "nitrodrp-site-down"
  alarm_description   = "https://${var.site_host} is failing its health check."
  namespace           = "AWS/Route53"
  metric_name         = "HealthCheckStatus"
  dimensions          = { HealthCheckId = aws_route53_health_check.site.id }
  statistic           = "Minimum"
  period              = 60
  evaluation_periods  = 2
  comparison_operator = "LessThanThreshold"
  threshold           = 1
  treat_missing_data  = "breaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
  ok_actions          = [aws_sns_topic.alerts.arn]
}

# --- Who did what? ------------------------------------------------------------------------------

resource "aws_s3_bucket" "trail" {
  bucket = "nitrodrp-cloudtrail-${data.aws_caller_identity.current.account_id}"
}

resource "aws_s3_bucket_public_access_block" "trail" {
  bucket                  = aws_s3_bucket.trail.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "trail" {
  bucket = aws_s3_bucket.trail.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "trail" {
  bucket = aws_s3_bucket.trail.id
  rule {
    id     = "expire-logs"
    status = "Enabled"
    filter {}
    expiration {
      days = 365
    }
  }
}

data "aws_iam_policy_document" "trail" {
  statement {
    sid       = "AclCheck"
    actions   = ["s3:GetBucketAcl"]
    resources = [aws_s3_bucket.trail.arn]
    principals {
      type        = "Service"
      identifiers = ["cloudtrail.amazonaws.com"]
    }
  }
  statement {
    sid       = "Write"
    actions   = ["s3:PutObject"]
    resources = ["${aws_s3_bucket.trail.arn}/AWSLogs/${data.aws_caller_identity.current.account_id}/*"]
    principals {
      type        = "Service"
      identifiers = ["cloudtrail.amazonaws.com"]
    }
    condition {
      test     = "StringEquals"
      variable = "s3:x-amz-acl"
      values   = ["bucket-owner-full-control"]
    }
  }
}

resource "aws_s3_bucket_policy" "trail" {
  bucket = aws_s3_bucket.trail.id
  policy = data.aws_iam_policy_document.trail.json
}

# Management events in every region. The first copy of these is free.
resource "aws_cloudtrail" "main" {
  name                          = "nitrodrp-trail"
  s3_bucket_name                = aws_s3_bucket.trail.id
  is_multi_region_trail         = true
  include_global_service_events = true
  enable_log_file_validation    = true
  depends_on                    = [aws_s3_bucket_policy.trail]
}

# Flags suspicious activity (stolen keys, odd API calls). Findings appear in the GuardDuty console.
resource "aws_guardduty_detector" "main" {
  enable = true
}
