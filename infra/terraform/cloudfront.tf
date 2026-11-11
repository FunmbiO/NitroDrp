# --- Downloads via CloudFront -----------------------------------------------------------------
# Uploads still go browser -> S3 with presigned multipart URLs. Downloads go through CloudFront:
# S3 -> CloudFront transfer is free and CloudFront's free tier covers 1 TB/month, versus S3's
# internet egress ($0.09/GB after 100 GB). The bucket stays private; only this distribution can read it.

resource "aws_cloudfront_origin_access_control" "files" {
  name                              = "${var.table_name}-files"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# Download URLs are signed by the API with the private key you generated; CloudFront
# verifies them with this public key. Without a valid, unexpired signature it returns 403.
resource "aws_cloudfront_public_key" "downloads" {
  name        = "${var.table_name}-downloads"
  encoded_key = var.cloudfront_public_key_pem
}

resource "aws_cloudfront_key_group" "downloads" {
  name  = "${var.table_name}-downloads"
  items = [aws_cloudfront_public_key.downloads.id]
}

# The signed URL carries response-content-disposition (the download filename), which S3 needs to
# receive. The signature params themselves (Expires, Signature, Key-Pair-Id) are not forwarded.
# TTLs are short so a deleted share's bytes don't linger at the edge.
resource "aws_cloudfront_cache_policy" "downloads" {
  name        = "${var.table_name}-downloads"
  min_ttl     = 0
  default_ttl = 300
  max_ttl     = 3600

  parameters_in_cache_key_and_forwarded_to_origin {
    enable_accept_encoding_gzip   = false
    enable_accept_encoding_brotli = false

    cookies_config {
      cookie_behavior = "none"
    }
    headers_config {
      header_behavior = "none"
    }
    query_strings_config {
      query_string_behavior = "whitelist"
      query_strings {
        items = ["response-content-disposition"]
      }
    }
  }
}

resource "aws_cloudfront_distribution" "downloads" {
  enabled      = true
  comment      = "NitroDrp downloads"
  price_class  = var.cloudfront_price_class
  http_version = "http2and3"

  origin {
    domain_name              = aws_s3_bucket.files.bucket_regional_domain_name
    origin_id                = "files"
    origin_access_control_id = aws_cloudfront_origin_access_control.files.id
  }

  default_cache_behavior {
    target_origin_id       = "files"
    viewer_protocol_policy = "https-only"
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    cache_policy_id        = aws_cloudfront_cache_policy.downloads.id
    trusted_key_groups     = [aws_cloudfront_key_group.downloads.id]
    compress               = false
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }
}

# Only this distribution (not the public) may read objects through the service principal.
data "aws_iam_policy_document" "cloudfront_read" {
  statement {
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.files.arn}/*"]

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.downloads.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "cloudfront_read" {
  bucket     = aws_s3_bucket.files.id
  policy     = data.aws_iam_policy_document.cloudfront_read.json
  depends_on = [aws_s3_bucket_public_access_block.files]
}
