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

V_PASSKEYS_ENABLED=false V_EMAIL_ENABLED=false V_EMAIL_FROM_ADDRESS="" V_PUBLIC_BASE_URL=""
yn=$(ask "Sign in with passkeys (Face ID, fingerprint) instead of typing the access key on each device? Needs https (Y/n)" "y")
case "$yn" in [nN]*) ;; *) V_PASSKEYS_ENABLED=true ;; esac

yn=$(ask "Email download links to recipients? Needs Amazon SES set up first, see docs/EMAIL.md (y/N)" "n")
case "$yn" in
  [yY]*)
    V_EMAIL_ENABLED=true
    V_EMAIL_FROM_ADDRESS=$(ask "Send mail from (an address at your verified domain, e.g. transfers@jadedmedia.ca)")
    case "$V_EMAIL_FROM_ADDRESS" in *?@?*.?*) ;; *) echo "That doesn't look like an email address." >&2; exit 1 ;; esac
    ;;
esac

if [ "$V_PASSKEYS_ENABLED" = true ] || [ "$V_EMAIL_ENABLED" = true ]; then
  V_PUBLIC_BASE_URL=$(ask "Public address of this site (passkeys are tied to it, and email links use it)" "${V_SITE_ADDRESS:+https://$V_SITE_ADDRESS}")
  case "$V_PUBLIC_BASE_URL" in https://*|http://*) V_PUBLIC_BASE_URL=${V_PUBLIC_BASE_URL%/} ;; *) echo "Give the full address, like https://drop.example.com" >&2; exit 1 ;; esac
  if [ "$V_PASSKEYS_ENABLED" = true ]; then
    case "$V_PUBLIC_BASE_URL" in https://*) ;; *) echo "Passkeys need an https:// address." >&2; exit 1 ;; esac
  fi
fi

V_PUSH_PUBLIC_KEY="" V_PUSH_PRIVATE_KEY="" V_PUSH_SUBJECT="" V_DIGEST_EMAIL_TO=""
yn=$(ask "Notifications to your phone when someone downloads a link, etc.? Needs https, see docs/NOTIFICATIONS.md (y/N)" "n")
case "$yn" in
  [yY]*)
    if [ -z "$V_PUBLIC_BASE_URL" ]; then
      V_PUBLIC_BASE_URL=$(ask "Public address of this site (phones only allow notifications on https)" "${V_SITE_ADDRESS:+https://$V_SITE_ADDRESS}")
      case "$V_PUBLIC_BASE_URL" in https://*) V_PUBLIC_BASE_URL=${V_PUBLIC_BASE_URL%/} ;; *) echo "Give the full https address, like https://drop.example.com" >&2; exit 1 ;; esac
    fi
    V_PUSH_SUBJECT=$(ask "Contact for the phone makers' push services (your email as mailto:you@example.com, or your https address)" "$V_PUBLIC_BASE_URL")
    case "$V_PUSH_SUBJECT" in mailto:?*@?*|https://?*) ;; *) echo "Give it as mailto:you@example.com or an https:// address." >&2; exit 1 ;; esac
    keys=$("$(dirname "$0")/make-vapid.sh")
    V_PUSH_PUBLIC_KEY=$(sed -n 's/^PUSH_PUBLIC_KEY=//p' <<<"$keys")
    V_PUSH_PRIVATE_KEY=$(sed -n 's/^PUSH_PRIVATE_KEY=//p' <<<"$keys")
    need "Push key" "$V_PUSH_PUBLIC_KEY"
    ;;
esac
if [ "$V_EMAIL_ENABLED" = true ]; then
  V_DIGEST_EMAIL_TO=$(ask "Email a short daily summary to (an address, blank for none)")
  case "$V_DIGEST_EMAIL_TO" in ""|*?@?*.?*) ;; *) echo "That doesn't look like an email address." >&2; exit 1 ;; esac
fi

if command -v openssl >/dev/null 2>&1; then
  V_API_KEY=$(openssl rand -hex 32)
else
  V_API_KEY=$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')
fi

export V_SENDER_NAME V_AWS_REGION V_S3_BUCKET V_DYNAMO_TABLE V_AWS_ACCESS_KEY_ID V_AWS_SECRET_ACCESS_KEY V_SITE_ADDRESS V_API_KEY V_EMAIL_ENABLED V_EMAIL_FROM_ADDRESS V_PUBLIC_BASE_URL V_PASSKEYS_ENABLED V_PUSH_PUBLIC_KEY V_PUSH_PRIVATE_KEY V_PUSH_SUBJECT V_DIGEST_EMAIL_TO
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
  /^# ?PASSKEYS_ENABLED=/    { if (ENVIRON["V_PASSKEYS_ENABLED"] == "true") print "PASSKEYS_ENABLED=true"; else print; next }
  /^# ?EMAIL_ENABLED=/       { if (ENVIRON["V_EMAIL_ENABLED"] == "true") print "EMAIL_ENABLED=true"; else print; next }
  /^# ?EMAIL_FROM_ADDRESS=/  { if (ENVIRON["V_EMAIL_ENABLED"] == "true") print "EMAIL_FROM_ADDRESS=" ENVIRON["V_EMAIL_FROM_ADDRESS"]; else print; next }
  /^# ?PUBLIC_BASE_URL=/     { if (ENVIRON["V_PUBLIC_BASE_URL"] != "") print "PUBLIC_BASE_URL=" ENVIRON["V_PUBLIC_BASE_URL"]; else print; next }
  /^# ?PUSH_PUBLIC_KEY=/     { if (ENVIRON["V_PUSH_PUBLIC_KEY"] != "") print "PUSH_PUBLIC_KEY=" ENVIRON["V_PUSH_PUBLIC_KEY"]; else print; next }
  /^# ?PUSH_PRIVATE_KEY=/    { if (ENVIRON["V_PUSH_PRIVATE_KEY"] != "") print "PUSH_PRIVATE_KEY=" ENVIRON["V_PUSH_PRIVATE_KEY"]; else print; next }
  /^# ?PUSH_SUBJECT=/        { if (ENVIRON["V_PUSH_SUBJECT"] != "") print "PUSH_SUBJECT=" ENVIRON["V_PUSH_SUBJECT"]; else print; next }
  /^# ?DIGEST_EMAIL_TO=/     { if (ENVIRON["V_DIGEST_EMAIL_TO"] != "") print "DIGEST_EMAIL_TO=" ENVIRON["V_DIGEST_EMAIL_TO"]; else print; next }
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
