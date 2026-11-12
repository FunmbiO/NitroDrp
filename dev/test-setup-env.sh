#!/usr/bin/env bash
# Tests deploy/setup-env.sh in a scratch copy, so it never touches your real .env.
#   bash dev/test-setup-env.sh
set -uo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
fail=0
ok()  { echo "PASS $1"; }
bad() { echo "FAIL $1"; fail=1; }
check() { if eval "$2"; then ok "$1"; else bad "$1"; fi; }

scratch() { T=$(mktemp -d); mkdir -p "$T/deploy"; cp "$ROOT/deploy/setup-env.sh" "$T/deploy/"; cp "$ROOT/.env.example" "$T/"; cp "$ROOT/docker-compose.yml" "$T/"; mkdir -p "$T/deploy"; cp "$ROOT/deploy/Caddyfile" "$T/deploy/"; echo "$T"; }
SECRET='wJalr/XUt+nFEMI/K7MDENG&bPx|RfiCYEXAMPLEKEY'
answers() { printf '%s\n' "Jaded Media" "us-east-2" "my-bucket" "" "AKIAEXAMPLE123456789" "$SECRET" "$1" "${2:-n}" "${@:3}"; }

T=$(scratch); cd "$T"
answers "drop.example.com" | ./deploy/setup-env.sh >out.txt 2>&1
check "writes .env" '[ -f .env ]'
check "mode is 600 (private)" '[ "$(stat -c %a .env)" = 600 ]'
check "bucket, region, table, hostname are set" 'grep -qx "S3_BUCKET=my-bucket" .env && grep -qx "AWS_REGION=us-east-2" .env && grep -qx "DYNAMO_TABLE=nitrodrp" .env && grep -qx "SITE_ADDRESS=drop.example.com" .env'
check "secret with / + & | survives byte for byte" 'grep -qxF "AWS_SECRET_ACCESS_KEY=$SECRET" .env'
check "API_KEY is 64 hex characters and is printed for saving" 'k=$(grep ^API_KEY= .env | cut -d= -f2); [ ${#k} -eq 64 ] && grep -q "$k" out.txt'
check "SITE_ADDRESS is uncommented (the bug that stops Caddy)" '! grep -q "^# *SITE_ADDRESS" .env'
check "other template lines are kept" 'grep -q "^CLOUDFRONT_DOMAIN=$" .env && grep -q "^TUNNEL_TOKEN=$" .env'
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  cfg=$(docker compose --profile public config 2>/dev/null)
  check "Docker Compose reads the values (hostname, bucket, secret)" 'echo "$cfg" | grep -q "SITE_ADDRESS: drop.example.com" && echo "$cfg" | grep -q "S3_BUCKET\|NitroDrp__S3__Bucket: my-bucket"'
else
  echo "SKIP Docker Compose reading the file (docker not installed here)"
fi

check "email stays off (and commented out) when you say no" '! grep -q "^EMAIL_ENABLED=" .env && grep -q "^# EMAIL_ENABLED=" .env'

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
answers "drop.example.com" y "transfers@example.com" "" | ./deploy/setup-env.sh >/dev/null 2>&1
check "email: enabled with from address, and the base URL defaults to the site" 'grep -qx "EMAIL_ENABLED=true" .env && grep -qx "EMAIL_FROM_ADDRESS=transfers@example.com" .env && grep -qx "PUBLIC_BASE_URL=https://drop.example.com" .env'
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  cfg=$(docker compose --profile public config 2>/dev/null)
  check "Docker Compose passes the email settings to the API" 'echo "$cfg" | grep -q "NitroDrp__Email__Enabled: \"true\"" && echo "$cfg" | grep -q "NitroDrp__Email__FromAddress: transfers@example.com" && echo "$cfg" | grep -q "NitroDrp__PublicBaseUrl: https://drop.example.com" && echo "$cfg" | grep -q "NitroDrp__Email__Region: us-east-2"'
fi

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
answers "drop.example.com" y "not-an-address" | ./deploy/setup-env.sh >/dev/null 2>&1
check "email: a bad from address is rejected" '[ ! -f .env ]'
answers "drop.example.com" y "transfers@example.com" "drop.example.com" | ./deploy/setup-env.sh >/dev/null 2>&1
check "email: a base URL without https:// is rejected" '[ ! -f .env ]'

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"; : > .env; echo keep > .env
printf 'n\n' | ./deploy/setup-env.sh >/dev/null 2>&1
check "refuses to overwrite an existing .env when you say no" '[ "$(cat .env)" = keep ]'

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
answers "https://drop.example.com/" | ./deploy/setup-env.sh >/dev/null 2>&1
check "rejects a hostname with https:// or slashes" '[ ! -f .env ]'
printf '%s\n' "Jaded Media" "us-east-2" "" | ./deploy/setup-env.sh >/dev/null 2>&1
check "rejects an empty bucket name" '[ ! -f .env ]'

cd "$ROOT"; rm -rf "$T"
[ $fail -eq 0 ] && echo "All checks passed." || { echo "Some checks failed."; exit 1; }
