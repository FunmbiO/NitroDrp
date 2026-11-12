#!/usr/bin/env bash
# Creates the private S3 bucket that holds Terraform's state, then writes infra/terraform/backend.hcl for it.
# Run once, with the same AWS credentials you use for Terraform (your terraform-admin user):
#
#   ./infra/bootstrap/state-bucket.sh                 # bucket nitrodrp-tfstate-<your account id>
#   ./infra/bootstrap/state-bucket.sh my-bucket-name  # or choose the name
#
# Safe to re-run: it only creates what's missing and re-applies the settings.
#
# Why a separate bucket from the one holding files: the app's AWS user can read and write everything in the
# files bucket, and its lifecycle rules delete objects. State needs neither.
set -euo pipefail
command -v aws >/dev/null || { echo "The AWS CLI isn't installed (https://aws.amazon.com/cli/)." >&2; exit 1; }

# One explicit region for every call below. Deliberately NOT inherited from your CLI's default: that might be
# a region your organization blocks, and a mismatch between it and the bucket's location constraint fails.
REGION=${STATE_REGION:-us-east-2}
export AWS_REGION=$REGION AWS_DEFAULT_REGION=$REGION
ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
BUCKET=${1:-nitrodrp-tfstate-$ACCOUNT}
OUT=${BACKEND_HCL_OUT:-$(cd "$(dirname "$0")/.." && pwd)/terraform/backend.hcl}

echo "Account $ACCOUNT, region $REGION, bucket $BUCKET"

if aws s3api head-bucket --bucket "$BUCKET" >/dev/null 2>&1; then
  echo "Bucket exists, re-applying settings."
elif [ "$REGION" = "us-east-1" ]; then
  aws s3api create-bucket --bucket "$BUCKET" >/dev/null
else
  aws s3api create-bucket --bucket "$BUCKET" --region "$REGION" \
    --create-bucket-configuration "LocationConstraint=$REGION" >/dev/null
fi

aws s3api put-public-access-block --bucket "$BUCKET" --public-access-block-configuration \
  BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true

# Every save of the state is kept, so a bad apply or an accidental delete can be rolled back.
aws s3api put-bucket-versioning --bucket "$BUCKET" --versioning-configuration Status=Enabled

aws s3api put-bucket-encryption --bucket "$BUCKET" --server-side-encryption-configuration \
  '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'

aws s3api put-bucket-policy --bucket "$BUCKET" --policy "{
  \"Version\": \"2012-10-17\",
  \"Statement\": [{
    \"Sid\": \"DenyInsecureTransport\",
    \"Effect\": \"Deny\",
    \"Principal\": \"*\",
    \"Action\": \"s3:*\",
    \"Resource\": [\"arn:aws:s3:::$BUCKET\", \"arn:aws:s3:::$BUCKET/*\"],
    \"Condition\": {\"Bool\": {\"aws:SecureTransport\": \"false\"}}
  }]
}"

# Keep old versions for 90 days, then let them go. The current state never expires.
aws s3api put-bucket-lifecycle-configuration --bucket "$BUCKET" --lifecycle-configuration '{
  "Rules": [{
    "ID": "expire-old-state-versions",
    "Status": "Enabled",
    "Filter": {},
    "NoncurrentVersionExpiration": {"NoncurrentDays": 90},
    "AbortIncompleteMultipartUpload": {"DaysAfterInitiation": 7}
  }]
}'

cat > "$OUT" <<HCL
bucket       = "$BUCKET"
key          = "nitrodrp/terraform.tfstate"
region       = "$REGION"
encrypt      = true
use_lockfile = true
HCL

echo
echo "Done. Wrote $OUT"
echo "Next: cd infra/terraform && terraform init -migrate-state -backend-config=backend.hcl"
