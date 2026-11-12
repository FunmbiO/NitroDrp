# State lives in a private S3 bucket (created once by infra/bootstrap/state-bucket.sh), not on one computer.
# The bucket and key come from backend.hcl, which is specific to your account and not committed:
#
#   terraform init -backend-config=backend.hcl
#
# use_lockfile (in backend.hcl) makes S3 itself refuse a second simultaneous apply. No DynamoDB table needed.
# CI and the offline tests run `terraform init -backend=false`, so they never touch it.
terraform {
  backend "s3" {}
}
