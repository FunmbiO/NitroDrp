#!/usr/bin/env python3
"""
Smoke test for a running local stack (docker compose -f docker-compose.yml -f docker-compose.dev.yml up).
Exercises the real API and storage end to end with no browser: create, multipart upload with a simulated
crash and resume, finalize, per-file download, streamed zip, byte-for-byte comparison, delete.

    python3 dev/smoke.py                          # http://localhost:8080
    python3 dev/smoke.py http://127.0.0.1:8080    # API directly
"""
import hashlib, io, json, os, sys, urllib.error, urllib.request as u, zipfile

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8080").rstrip("/")
API_KEY = os.environ.get("API_KEY", "dev-access-key-change-me")
MB = 1024 * 1024


def call(method, path, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    req = u.Request(BASE + "/api" + path, method=method, data=data, headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with u.urlopen(req) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read()
        return e.code, (json.loads(raw) if raw else None)


def put_part(url, data):
    # Browsers send parts as plain binary. Declare that, or some emulators try to parse the body as a web form.
    req = u.Request(url, method="PUT", data=data, headers={"Content-Type": "application/octet-stream"})
    with u.urlopen(req) as r:
        return r.headers["ETag"]


def get(url):
    with u.urlopen(BASE + url if url.startswith("/") else url) as r:
        return r.read()


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        sys.exit(1)


status, health = call("GET", "/health")
check(status == 200, f"API reachable at {BASE}")

big = os.urandom(40_000_000)   # 3 parts at 16 MiB
small = os.urandom(300_000)    # 1 part
files = {"big.bin": big, "small.bin": small}
status, created = call("POST", "/shares", {"files": [{"name": n, "size": len(d)} for n, d in files.items()], "expiresInDays": 1, "downloadLimit": 5},
                       {"X-Api-Key": API_KEY})
check(status == 201, "created a share (access key accepted)")
token, owner = created["token"], {"X-Owner-Key": created["ownerKey"]}
part_size = created["partSize"]
by_name = {f["name"]: f for f in created["files"]}


def upload_part(name, n):
    f = by_name[name]
    _, p = call("POST", f"/shares/{token}/files/{f['id']}/presign", {"partNumbers": [n]}, owner)
    return put_part(p["urls"][0]["url"], files[name][(n - 1) * part_size: n * part_size])


for n in (1, 2):  # upload two of big's three parts, then "crash"
    upload_part("big.bin", n)
status, st = call("GET", f"/shares/{token}/upload", None, owner)
listed = {f["name"]: f for f in st["files"]}
check(sorted(p["partNumber"] for p in listed["big.bin"]["parts"]) == [1, 2], "after a simulated crash, storage reports parts 1 and 2 (ListParts works)")

parts = listed["big.bin"]["parts"] + [{"partNumber": 3, "eTag": upload_part("big.bin", 3)}]
check(call("POST", f"/shares/{token}/files/{by_name['big.bin']['id']}/complete", {"parts": parts}, owner)[0] == 204, "resumed and completed big.bin")
small_parts = [{"partNumber": 1, "eTag": upload_part("small.bin", 1)}]
check(call("POST", f"/shares/{token}/files/{by_name['small.bin']['id']}/complete", {"parts": small_parts}, owner)[0] == 204, "completed small.bin")
check(call("POST", f"/shares/{token}/finalize", None, owner)[0] == 204, "finalized (storage sizes verified)")

_, view = call("GET", f"/shares/{token}")
check(view["fileCount"] == 2 and view["downloadsRemaining"] == 5, "public view shows 2 files and 5 downloads left")

_, dl = call("POST", f"/shares/{token}/download", {"fileId": by_name["small.bin"]["id"]})
got = get(dl["files"][0]["url"])
check(hashlib.sha256(got).digest() == hashlib.sha256(small).digest(), "single-file download is byte-identical")

_, zl = call("POST", f"/shares/{token}/zip-link", {})
z = zipfile.ZipFile(io.BytesIO(get(zl["url"])))
check(z.testzip() is None and sorted(z.namelist()) == ["big.bin", "small.bin"], "zip is valid and holds both files")
check(all(hashlib.sha256(z.read(n)).digest() == hashlib.sha256(d).digest() for n, d in files.items()), "every file in the zip is byte-identical (incl. the resumed 40 MB one)")

_, view = call("GET", f"/shares/{token}")
check(view["downloadsRemaining"] == 3, "download counter: 1 file + 1 zip = 2 used")
check(call("DELETE", f"/shares/{token}", None, owner)[0] == 204 and call("GET", f"/shares/{token}")[0] == 404, "deleted; link now 404s")
print("\nAll checks passed.")
