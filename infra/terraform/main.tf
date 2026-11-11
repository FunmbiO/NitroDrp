# --- File storage -----------------------------------------------------------------------------

resource "aws_s3_bucket" "files" {
  bucket = var.bucket_name
}

resource "aws_s3_bucket_public_access_block" "files" {
  bucket                  = aws_s3_bucket.files.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "files" {
  bucket = aws_s3_bucket.files.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# Browsers PUT parts and GET files directly using presigned URLs, so the bucket must allow
# those origins and expose the ETag header (the uploader needs it to complete the multipart upload).
resource "aws_s3_bucket_cors_configuration" "files" {
  bucket = aws_s3_bucket.files.id
  cors_rule {
    allowed_methods = ["PUT", "GET"]
    allowed_origins = var.allowed_origins
    allowed_headers = ["*"]
    expose_headers  = ["ETag"]
    max_age_seconds = 3600
  }
}

# The API writes each share under d<expiry-days>/<token>/<file>, so expiry is just a prefix rule.
# Lifecycle runs once a day and the API already refuses expired links, so the extra day only
# means bytes may linger a little past the link's expiry, never that a link works longer.
resource "aws_s3_bucket_lifecycle_configuration" "files" {
  bucket = aws_s3_bucket.files.id

  dynamic "rule" {
    for_each = toset(var.expiry_days)
    content {
      id     = "expire-d${rule.value}"
      status = "Enabled"
      filter {
        prefix = "d${rule.value}/"
      }
      expiration {
        days = rule.value + 1
      }
    }
  }

  rule {
    id     = "abort-stale-multipart"
    status = "Enabled"
    filter {}
    abort_incomplete_multipart_upload {
      days_after_initiation = 2
    }
  }
}

# --- Link metadata ----------------------------------------------------------------------------

resource "aws_dynamodb_table" "shares" {
  name         = var.table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "pk"
  range_key    = "sk"

  attribute {
    name = "pk"
    type = "S"
  }
  attribute {
    name = "sk"
    type = "S"
  }

  ttl {
    attribute_name = "ttl"
    enabled        = true
  }

  point_in_time_recovery {
    enabled = false
  }
}

# --- Credentials for the home-server API ------------------------------------------------------
# Least privilege: this identity is also what signs presigned URLs, so browsers can do no more than this.
# Create its access key in the console (not here) so the secret never lands in Terraform state.

resource "aws_iam_user" "api" {
  name = "nitrodrp-api"
}

data "aws_iam_policy_document" "api" {
  statement {
    actions = [
      "s3:PutObject",
      "s3:GetObject",
      "s3:DeleteObject",
      "s3:AbortMultipartUpload",
      "s3:ListMultipartUploadParts",
    ]
    resources = ["${aws_s3_bucket.files.arn}/*"]
  }
  statement {
    actions   = ["s3:ListBucket"] # lets HeadObject return 404 instead of 403 for missing keys
    resources = [aws_s3_bucket.files.arn]
  }
  dynamic "statement" {
    # Lets "Delete link now" purge the edge cache. Scoped to this one distribution, and only
    # present when there is one.
    for_each = var.enable_cloudfront ? [1] : []
    content {
      actions   = ["cloudfront:CreateInvalidation"]
      resources = [aws_cloudfront_distribution.downloads[0].arn]
    }
  }
  statement {
    actions   = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem", "dynamodb:DeleteItem", "dynamodb:Query", "dynamodb:BatchWriteItem"]
    resources = [aws_dynamodb_table.shares.arn]
  }
}

resource "aws_iam_user_policy" "api" {
  name   = "nitrodrp-api"
  user   = aws_iam_user.api.name
  policy = data.aws_iam_policy_document.api.json
}

# --- Cost guardrail ---------------------------------------------------------------------------

resource "aws_budgets_budget" "monthly" {
  name         = "nitrodrp-monthly"
  budget_type  = "COST"
  limit_amount = tostring(var.monthly_budget_usd)
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 80
    threshold_type             = "PERCENTAGE"
    notification_type          = "ACTUAL"
    subscriber_email_addresses = [var.budget_email]
  }
  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    notification_type          = "FORECASTED"
    subscriber_email_addresses = [var.budget_email]
  }
}
