output "bucket_name" {
  value = aws_s3_bucket.files.bucket
}

output "table_name" {
  value = aws_dynamodb_table.shares.name
}

output "iam_user_name" {
  description = "Create an access key for this user in the IAM console and put it in .env."
  value       = aws_iam_user.api.name
}

output "cloudfront_domain" {
  description = "Set as NitroDrp__CloudFront__Domain."
  value       = aws_cloudfront_distribution.downloads.domain_name
}

output "cloudfront_key_pair_id" {
  description = "Set as NitroDrp__CloudFront__KeyPairId."
  value       = aws_cloudfront_public_key.downloads.id
}
