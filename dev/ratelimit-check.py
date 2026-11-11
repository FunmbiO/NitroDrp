#!/usr/bin/env python3
"""
Checks that the API's rate limits can't be dodged by forging client-IP headers.

    python3 dev/ratelimit-check.py https://send.jadedmedia.ca

Sends 14 requests to a password endpoint (limit: 10 a minute per client), each with a different forged
CF-Connecting-IP and X-Caddy-Client-IP. If forged headers were trusted, every request would look like a new
visitor and none would be limited.

  - Behind Caddy (Lightsail) or Cloudflare: PASS. The proxy overwrites those headers, so you get 429s.
  - Against http://localhost:8080: FAIL, and that's expected. Nothing sits in front to overwrite the header,
    but that port is bound to 127.0.0.1, so only you can reach it.

Running it uses up your own allowance on that endpoint for about a minute. Harmless.
"""
import json, sys, urllib.error, urllib.request as u

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8080").rstrip("/")
codes = []
for i in range(14):
    req = u.Request(
        f"{BASE}/api/shares/ratelimitcheck/unlock", method="POST", data=json.dumps({"password": "x"}).encode(),
        headers={"Content-Type": "application/json", "CF-Connecting-IP": f"198.51.100.{i + 1}", "X-Caddy-Client-IP": f"203.0.113.{i + 1}"},
    )
    try:
        with u.urlopen(req) as r:
            codes.append(r.status)
    except urllib.error.HTTPError as e:
        codes.append(e.code)

print("status codes:", codes)
limited = codes.count(429)
if limited:
    print(f"PASS: {limited} of 14 requests were rate-limited despite forged headers.")
    sys.exit(0)
print("FAIL: no request was limited, so the forged headers were believed.")
print("      (Expected only when talking to the container directly, e.g. http://localhost:8080.)")
sys.exit(1)
