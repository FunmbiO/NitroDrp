# Offline tests (no AWS account or credentials): a mocked AWS provider plans the stack in each mode.
#   terraform init -backend=false && terraform test

mock_provider "aws" {}

variables {
  bucket_name     = "test-nitrodrp-files"
  allowed_origins = ["https://drop.example.com"]
  budget_email    = "you@example.com"
}

run "s3_only_creates_no_cloudfront" {
  command = plan

  variables {
    enable_cloudfront = false
  }

  assert {
    condition     = length(aws_cloudfront_distribution.downloads) == 0
    error_message = "S3-only mode must not create a distribution."
  }

  assert {
    condition     = length(aws_cloudfront_public_key.downloads) == 0 && length(aws_cloudfront_key_group.downloads) == 0 && length(aws_cloudfront_origin_access_control.files) == 0 && length(aws_cloudfront_cache_policy.downloads) == 0
    error_message = "S3-only mode must not create any CloudFront resources."
  }

  assert {
    condition     = length(aws_s3_bucket_policy.cloudfront_read) == 0
    error_message = "Without CloudFront the bucket needs no CloudFront read policy."
  }

  assert {
    condition     = output.cloudfront_domain == null && output.cloudfront_distribution_id == null
    error_message = "CloudFront outputs must be null when it is off."
  }

  assert {
    condition     = aws_s3_bucket.files.bucket == "test-nitrodrp-files" && aws_dynamodb_table.shares.name == "nitrodrp"
    error_message = "The core stack (bucket and table) must still be planned."
  }
}

run "s3_only_needs_no_public_key" {
  command = plan

  variables {
    enable_cloudfront         = false
    cloudfront_public_key_pem = ""
  }
}

run "cloudfront_mode_creates_the_distribution" {
  command = plan

  variables {
    enable_cloudfront         = true
    cloudfront_public_key_pem = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA\n-----END PUBLIC KEY-----"
  }

  assert {
    condition     = length(aws_cloudfront_distribution.downloads) == 1 && length(aws_cloudfront_key_group.downloads) == 1 && length(aws_s3_bucket_policy.cloudfront_read) == 1
    error_message = "CloudFront mode must create the distribution, key group and bucket policy."
  }

  assert {
    condition     = aws_cloudfront_distribution.downloads[0].price_class == "PriceClass_100"
    error_message = "The cheapest price class should be the default."
  }
}

run "cloudfront_mode_rejects_a_missing_key" {
  command = plan

  variables {
    enable_cloudfront         = true
    cloudfront_public_key_pem = ""
  }

  expect_failures = [aws_cloudfront_public_key.downloads]
}

run "cloudfront_mode_rejects_a_private_key" {
  command = plan

  variables {
    enable_cloudfront         = true
    cloudfront_public_key_pem = "-----BEGIN RSA PRIVATE KEY-----\nMIIEow\n-----END RSA PRIVATE KEY-----"
  }

  expect_failures = [aws_cloudfront_public_key.downloads]
}
