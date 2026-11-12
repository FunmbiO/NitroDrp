#!/usr/bin/env bash
# Proves the remote-state setup works, without touching a real AWS account: runs the bootstrap script and a
# state migration against an S3 emulator (moto) in a scratch Terraform project that uses the same backend.tf.
#
#   pip install "moto[server]" awscli
#   moto_server -p 5000 &
#   bash infra/test-state-backend.sh
set -uo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
EP=${STATE_TEST_ENDPOINT:-http://127.0.0.1:5000}
export AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_ENDPOINT_URL=$EP
fail=0
ok()  { echo "PASS $1"; }
bad() { echo "FAIL $1"; fail=1; }
check() { if eval "$2"; then ok "$1"; else bad "$1"; fi; }

W=$(mktemp -d); trap 'rm -rf "$W"' EXIT
BUCKET=nitrodrp-tfstate-test-$RANDOM

# 1. the bootstrap script
BACKEND_HCL_OUT=$W/backend.hcl "$ROOT/infra/bootstrap/state-bucket.sh" "$BUCKET" >"$W/bootstrap.log" 2>&1
check "bootstrap creates the bucket and backend.hcl" '[ -f "$W/backend.hcl" ] && grep -q "bucket       = \"$BUCKET\"" "$W/backend.hcl"'
check "bucket is versioned and encrypted" '[ "$(aws s3api get-bucket-versioning --bucket $BUCKET --query Status --output text)" = Enabled ] && aws s3api get-bucket-encryption --bucket $BUCKET >/dev/null 2>&1'
check "bucket blocks public access" 'aws s3api get-public-access-block --bucket $BUCKET --query PublicAccessBlockConfiguration.BlockPublicPolicy --output text | grep -q True'

check "bucket policy denies plain-HTTP access" 'aws s3api get-bucket-policy --bucket $BUCKET --query Policy --output text | grep -q DenyInsecureTransport'
# The emulator speaks plain HTTP, which that policy (correctly) refuses, so remove it for the rest of this test.
# Real AWS is reached over HTTPS and is unaffected.
aws s3api delete-bucket-policy --bucket "$BUCKET"

# 2. a scratch project: begins with LOCAL state (like yours), then migrates
P=$W/project; mkdir -p "$P"
cat > "$P/main.tf" <<'TF'
terraform {
  required_version = ">= 1.10"
}
resource "terraform_data" "existing" {
  input = "created before the migration"
}
resource "terraform_data" "slow" {
  count = var.slow ? 1 : 0
  provisioner "local-exec" { command = "sleep 12" }
}
variable "slow" {
  type    = bool
  default = false
}
TF
( cd "$P" && terraform init -no-color >/dev/null 2>&1 && terraform apply -auto-approve -no-color >/dev/null 2>&1 )
check "local state exists before migrating" '[ -f "$P/terraform.tfstate" ]'
SERIAL=$(python3 -c "import json;print(json.load(open('$P/terraform.tfstate'))['serial'])")

# the emulator needs a few overrides a real AWS account does not
cat > "$P/emulator.hcl" <<HCL
endpoints                   = { s3 = "$EP" }
skip_credentials_validation = true
skip_requesting_account_id  = true
skip_metadata_api_check     = true
skip_region_validation      = true
use_path_style              = true
HCL
cp "$ROOT/infra/terraform/backend.tf" "$P/backend.tf"
cp "$W/backend.hcl" "$P/backend.hcl"

# 3. the exact migration command from the guide
( cd "$P" && terraform init -migrate-state -force-copy -backend-config=backend.hcl -backend-config=emulator.hcl -no-color >"$W/migrate.log" 2>&1 )
check "migration succeeds" 'grep -q "Successfully configured the backend" "$W/migrate.log"'
check "state object is now in the bucket" 'aws s3api head-object --bucket $BUCKET --key nitrodrp/terraform.tfstate >/dev/null 2>&1'
check "the state's serial and resource carried over intact" '[ "$(aws s3 cp s3://$BUCKET/nitrodrp/terraform.tfstate - 2>/dev/null | python3 -c "import sys,json;d=json.load(sys.stdin);print(d[\"serial\"], d[\"resources\"][0][\"name\"])")" = "$SERIAL existing" ]'

# 4. lose the local copy entirely, as if the PC died, and recover from S3
rm -rf "$P/terraform.tfstate" "$P/terraform.tfstate.backup" "$P/.terraform"
( cd "$P" && terraform init -backend-config=backend.hcl -backend-config=emulator.hcl -no-color >/dev/null 2>&1 )
check "after deleting every local file, init pulls the state back" '( cd "$P" && terraform state list -no-color 2>/dev/null | grep -qx "terraform_data.existing" )'
check "plan sees no changes (nothing would be recreated)" '( cd "$P" && terraform plan -detailed-exitcode -no-color >/dev/null 2>&1 ); [ $? -eq 0 ]'

# 5. history: a second apply keeps the previous version
( cd "$P" && terraform apply -auto-approve -var slow=false -replace=terraform_data.existing -no-color >/dev/null 2>&1 )
check "bucket keeps old versions of the state" '[ "$(aws s3api list-object-versions --bucket $BUCKET --prefix nitrodrp/terraform.tfstate --query "length(Versions)" --output text)" -ge 2 ]'

# 6. locking: a second run must be refused while the first holds the lock
( cd "$P" && terraform apply -auto-approve -var slow=true -no-color >/dev/null 2>&1 ) &
sleep 5
OUT=$( cd "$P" && terraform plan -lock-timeout=0s -no-color 2>&1 )
wait
check "a second run is refused while one is in progress (state lock)" 'echo "$OUT" | grep -qi "Error acquiring the state lock"'

[ $fail -eq 0 ] && echo "All checks passed." || { echo "Some checks failed."; exit 1; }
