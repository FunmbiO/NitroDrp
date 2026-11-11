#!/usr/bin/env bash
# Writes .env for you by asking a few questions, instead of editing the file by hand.
#   ./deploy/setup-env.sh
# Generates a fresh API_KEY, keeps every other setting from .env.example, and makes the file private (600).
# The AWS secret is typed hidden. Press Enter to accept a [default].
set -euo pipefail
cd "$(dirname "$0")/.."

TEMPLATE=.env.example
OUT=.env
[ -f "$TEMPLATE" ] || { echo "Run this from a NitroDrp checkout ($TEMPLATE not found)." >&2; exit 1; }

if [ -e "$OUT" ]; then
  read -rp "$OUT already exists. Overwrite it? [y/N] " yn
  case "$yn" in [yY]*) ;; *) echo "Left $OUT alone."; exit 1 ;; esac
fi

ask() { # ask "prompt" [default]
  local v
  read -rp "$1${2:+ [$2]}: " v
  printf '%s' "${v:-${2:-}}"
}
need() { [ -n "$2" ] || { echo "$1 can't be empty." >&2; exit 1; }; }

V_SENDER_NAME=$(ask "Name recipients see ('<name> sent you files')" "Jaded Media")
V_AWS_REGION=$(ask "AWS region" "us-east-2")
V_S3_BUCKET=$(ask "S3 bucket name (terraform output bucket_name)")
need "Bucket name" "$V_S3_BUCKET"
V_DYNAMO_TABLE=$(ask "DynamoDB table" "nitrodrp")
V_AWS_ACCESS_KEY_ID=$(ask "AWS access key id for the nitrodrp-api user (starts with AKIA)")
need "Access key id" "$V_AWS_ACCESS_KEY_ID"
read -rsp "AWS secret access key (hidden): " V_AWS_SECRET_ACCESS_KEY; echo
need "Secret access key" "$V_AWS_SECRET_ACCESS_KEY"
V_SITE_ADDRESS=$(ask "Public hostname for HTTPS, e.g. drop.example.com (blank if not using Caddy)")
case "$V_SITE_ADDRESS" in
  *://*|*/*|*" "*) echo "Give just the hostname, like drop.example.com (no https://, slashes or spaces)." >&2; exit 1 ;;
esac

if command -v openssl >/dev/null 2>&1; then
  V_API_KEY=$(openssl rand -hex 32)
else
  V_API_KEY=$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')
fi

export V_SENDER_NAME V_AWS_REGION V_S3_BUCKET V_DYNAMO_TABLE V_AWS_ACCESS_KEY_ID V_AWS_SECRET_ACCESS_KEY V_SITE_ADDRESS V_API_KEY
umask 077
# Values go through the environment (not the command line or a sed expression), so characters like / + & | in
# a secret can't break anything.
awk '
  /^SENDER_NAME=/            { print "SENDER_NAME=" ENVIRON["V_SENDER_NAME"]; next }
  /^API_KEY=/                { print "API_KEY=" ENVIRON["V_API_KEY"]; next }
  /^AWS_REGION=/             { print "AWS_REGION=" ENVIRON["V_AWS_REGION"]; next }
  /^S3_BUCKET=/              { print "S3_BUCKET=" ENVIRON["V_S3_BUCKET"]; next }
  /^DYNAMO_TABLE=/           { print "DYNAMO_TABLE=" ENVIRON["V_DYNAMO_TABLE"]; next }
  /^AWS_ACCESS_KEY_ID=/      { print "AWS_ACCESS_KEY_ID=" ENVIRON["V_AWS_ACCESS_KEY_ID"]; next }
  /^AWS_SECRET_ACCESS_KEY=/  { print "AWS_SECRET_ACCESS_KEY=" ENVIRON["V_AWS_SECRET_ACCESS_KEY"]; next }
  /^# ?SITE_ADDRESS=/        { if (ENVIRON["V_SITE_ADDRESS"] != "") print "SITE_ADDRESS=" ENVIRON["V_SITE_ADDRESS"]; else print; next }
  { print }
' "$TEMPLATE" > "$OUT"
chmod 600 "$OUT"

echo
echo "Wrote $OUT (private to you)."
echo
echo "Your API_KEY (the access key the upload page asks for). Save it in a password manager NOW:"
echo "  $V_API_KEY"
echo
echo "Check it, then start:"
echo "  docker compose --profile public config | grep -E 'SITE_ADDRESS|S3_BUCKET'"
echo "  docker compose --profile public up -d --build"
