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
  description = "Set as CLOUDFRONT_DOMAIN. Null when enable_cloudfront is false."
  value       = one(aws_cloudfront_distribution.downloads[*].domain_name)
}

output "cloudfront_key_pair_id" {
  description = "Set as NitroDrp__CloudFront__KeyPairId."
  value       = one(aws_cloudfront_public_key.downloads[*].id)
}

output "cloudfront_distribution_id" {
  description = "Set as NitroDrp__CloudFront__DistributionId so deleting a link purges the edge cache."
  value       = one(aws_cloudfront_distribution.downloads[*].id)
}

output "email_dkim_records" {
  description = "Add these three CNAME records at your DNS host so SES can verify the domain and sign mail. Empty when email is off."
  value = [for t in local.dkim_tokens : {
    name  = "${t}._domainkey.${var.email_domain}"
    type  = "CNAME"
    value = "${t}.dkim.amazonses.com"
  }]
}

output "email_identity_arn" {
  description = "The verified-sending identity. Null when email is off."
  value       = one(aws_sesv2_email_identity.sender[*].arn)
}
