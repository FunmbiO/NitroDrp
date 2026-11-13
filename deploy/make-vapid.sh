#!/usr/bin/env bash
# Makes the key pair that lets this server send push notifications (a "VAPID" key pair). Prints two lines:
#   PUSH_PUBLIC_KEY=...
#   PUSH_PRIVATE_KEY=...
# Put both in .env. Keep the private one secret, like the API_KEY. Needs only openssl.
#   ./deploy/make-vapid.sh
# If you ever make a new pair, every device has to turn notifications on again (the old subscriptions stop working).
set -euo pipefail

command -v openssl >/dev/null 2>&1 || { echo "openssl is needed to make the keys (apt install openssl)." >&2; exit 1; }

# Raw hex -> base64url, with no padding (the form browsers and servers use for these keys).
b64url() { printf '%b' "$(sed 's/../\\x&/g' <<<"$1")" | openssl base64 -A | tr '+/' '-_' | tr -d '='; }

text=$(openssl ecparam -name prime256v1 -genkey -noout | openssl ec -noout -text 2>/dev/null)
priv=$(awk '/^priv:/{f=1;next} /^pub:/{f=0} f' <<<"$text" | tr -d ' :\n')
pub=$(awk '/^pub:/{f=1;next} /^ASN1|^NIST/{f=0} f' <<<"$text" | tr -d ' :\n')
priv=${priv: -64}   # a leading 00 byte is sometimes printed; the key itself is the last 32 bytes

if [ ${#priv} -ne 64 ] || [ ${#pub} -ne 130 ] || [ "${pub:0:2}" != 04 ]; then
  echo "Couldn't read the key openssl made. Is this a very old openssl?" >&2
  exit 1
fi
echo "PUSH_PUBLIC_KEY=$(b64url "$pub")"
echo "PUSH_PRIVATE_KEY=$(b64url "$priv")"
