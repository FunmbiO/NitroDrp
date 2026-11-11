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
