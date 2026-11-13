#!/usr/bin/env bash
# Tests deploy/setup-env.sh in a scratch copy, so it never touches your real .env.
#   bash dev/test-setup-env.sh
set -uo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
fail=0
ok()  { echo "PASS $1"; }
bad() { echo "FAIL $1"; fail=1; }
check() { if eval "$2"; then ok "$1"; else bad "$1"; fi; }

scratch() { T=$(mktemp -d); mkdir -p "$T/deploy"; cp "$ROOT/deploy/setup-env.sh" "$ROOT/deploy/make-vapid.sh" "$T/deploy/"; cp "$ROOT/.env.example" "$T/"; cp "$ROOT/docker-compose.yml" "$T/"; mkdir -p "$T/deploy"; cp "$ROOT/deploy/Caddyfile" "$T/deploy/"; echo "$T"; }
SECRET='wJalr/XUt+nFEMI/K7MDENG&bPx|RfiCYEXAMPLEKEY'
# answers <site> [passkeys y/n] [email y/n] [more answers...]   (passkeys and email default to "n")
# The notification questions come last: push (y/n), then the push contact if yes, then (only with email on) who gets the summary.
# Set PUSH=y, PUSH_SUBJECT=... and DIGEST=... to answer them; a set-but-empty value means "press Enter".
answers() { printf '%s\n' "Jaded Media" "us-east-2" "my-bucket" "" "AKIAEXAMPLE123456789" "$SECRET" "$1" "${2:-n}" "${3:-n}" "${@:4}" "${PUSH:-n}" ${PUSH_SUBJECT+"$PUSH_SUBJECT"} ${DIGEST+"$DIGEST"}; }

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

check "passkeys stay off (and commented out) when you say no" '! grep -q "^PASSKEYS_ENABLED=" .env && grep -q "^# PASSKEYS_ENABLED=" .env'
check "email stays off (and commented out) when you say no" '! grep -q "^EMAIL_ENABLED=" .env && grep -q "^# EMAIL_ENABLED=" .env'

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
answers "drop.example.com" n y "transfers@example.com" "" | ./deploy/setup-env.sh >/dev/null 2>&1
check "email: enabled with from address, and the base URL defaults to the site" 'grep -qx "EMAIL_ENABLED=true" .env && grep -qx "EMAIL_FROM_ADDRESS=transfers@example.com" .env && grep -qx "PUBLIC_BASE_URL=https://drop.example.com" .env'
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  cfg=$(docker compose --profile public config 2>/dev/null)
  check "Docker Compose passes the email settings to the API" 'echo "$cfg" | grep -q "NitroDrp__Email__Enabled: \"true\"" && echo "$cfg" | grep -q "NitroDrp__Email__FromAddress: transfers@example.com" && echo "$cfg" | grep -q "NitroDrp__PublicBaseUrl: https://drop.example.com" && echo "$cfg" | grep -q "NitroDrp__Email__Region: us-east-2"'
fi

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
answers "drop.example.com" y n "" | ./deploy/setup-env.sh >/dev/null 2>&1
check "passkeys: enabled, and the public address defaults to the site" 'grep -qx "PASSKEYS_ENABLED=true" .env && grep -qx "PUBLIC_BASE_URL=https://drop.example.com" .env && grep -q "^# EMAIL_ENABLED=" .env'
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  cfg=$(docker compose --profile public config 2>/dev/null)
  check "Docker Compose passes the passkey setting to the API" 'echo "$cfg" | grep -q "NitroDrp__Auth__Passkeys: \"true\""'
fi

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
answers "drop.example.com" y n "http://drop.example.com" | ./deploy/setup-env.sh >/dev/null 2>&1
check "passkeys: an http:// address is rejected (a passkey needs https)" '[ ! -f .env ]'

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
answers "drop.example.com" n y "not-an-address" | ./deploy/setup-env.sh >/dev/null 2>&1
check "email: a bad from address is rejected" '[ ! -f .env ]'
answers "drop.example.com" n y "transfers@example.com" "drop.example.com" | ./deploy/setup-env.sh >/dev/null 2>&1
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
T=$(scratch); cd "$T"
PUSH=y PUSH_SUBJECT="mailto:me@example.com" answers "drop.example.com" y n "" | ./deploy/setup-env.sh >out.txt 2>&1
check "push: makes a key pair and writes it with the contact" 'grep -qx "PUSH_SUBJECT=mailto:me@example.com" .env && grep -Eq "^PUSH_PUBLIC_KEY=[A-Za-z0-9_-]{87}$" .env && grep -Eq "^PUSH_PRIVATE_KEY=[A-Za-z0-9_-]{43}$" .env'
check "push: the private key is not printed to the screen" '! grep -q "$(grep ^PUSH_PRIVATE_KEY= .env | cut -d= -f2)" out.txt'
check "push: the file stays private (600)" '[ "$(stat -c %a .env)" = 600 ]'
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  cfg=$(docker compose --profile public config 2>/dev/null)
  check "Docker Compose passes the push settings to the API" 'echo "$cfg" | grep -q "NitroDrp__Notifications__Push__PublicKey: $(grep ^PUSH_PUBLIC_KEY= .env | cut -d= -f2)" && echo "$cfg" | grep -q "NitroDrp__Notifications__Push__Subject: mailto:me@example.com"'
fi

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
PUSH=y PUSH_SUBJECT="not a contact" answers "drop.example.com" y n "" | ./deploy/setup-env.sh >/dev/null 2>&1
check "push: a contact that is neither mailto: nor https is rejected" '[ ! -f .env ]'
PUSH=y PUSH_SUBJECT="" answers "drop.example.com" y n "" | ./deploy/setup-env.sh >/dev/null 2>&1
check "push: the contact defaults to the site address" 'grep -qx "PUSH_SUBJECT=https://drop.example.com" .env'

cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
DIGEST="me@example.com" answers "drop.example.com" y y "transfers@example.com" "" | ./deploy/setup-env.sh >/dev/null 2>&1
check "summary: with email on, the daily summary address is saved" 'grep -qx "DIGEST_EMAIL_TO=me@example.com" .env && grep -q "^# PUSH_PUBLIC_KEY=" .env'
cd "$ROOT"; rm -rf "$T"
T=$(scratch); cd "$T"
DIGEST="nope" answers "drop.example.com" y y "transfers@example.com" "" | ./deploy/setup-env.sh >/dev/null 2>&1
check "summary: a bad address is rejected" '[ ! -f .env ]'

cd "$ROOT"; rm -rf "$T"
# the key tool on its own
k1=$("$ROOT/deploy/make-vapid.sh"); k2=$("$ROOT/deploy/make-vapid.sh")
check "make-vapid: prints a 65-byte public and a 32-byte private key (base64url)" 'echo "$k1" | grep -Eq "^PUSH_PUBLIC_KEY=[A-Za-z0-9_-]{87}$" && echo "$k1" | grep -Eq "^PUSH_PRIVATE_KEY=[A-Za-z0-9_-]{43}$"'
check "make-vapid: the public key starts with the uncompressed-point marker (B...)" 'echo "$k1" | grep -q "^PUSH_PUBLIC_KEY=B"'
check "make-vapid: every run is a new pair" '[ "$k1" != "$k2" ]'

[ $fail -eq 0 ] && echo "All checks passed." || { echo "Some checks failed."; exit 1; }
