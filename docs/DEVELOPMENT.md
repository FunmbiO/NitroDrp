# Development

Running, testing and changing NitroDrp. How it's built: [ARCHITECTURE.md](ARCHITECTURE.md). Deploying it:
[DEPLOY.md](DEPLOY.md).

## What's where

| Folder | What it holds |
|---|---|
| `api/src/NitroDrp.Api` | The ASP.NET Core API: endpoints, `ShareService` (all the rules), S3 and DynamoDB adapters |
| `api/tests` | xUnit tests: rules against in-memory fakes, plus the real HTTP pipeline |
| `web/src` | The React app, including the in-browser backend used by the demo |
| `web/mock` | A dev-only mock API so the UI can run with no AWS and no Docker |
| `web/e2e` | Browser tests and the README screenshot script (Playwright) |
| `infra/terraform` | Bucket, table, IAM, optional CloudFront, budget; offline tests in `tests/` |
| `infra/bootstrap` | One-off script that creates the Terraform state bucket |
| `deploy` | The Caddyfile and the script that writes `.env` for you |
| `dev` | Smoke test, rate-limit check, link checker and script tests |
| `docs` | These guides and the README screenshots |

## Try the UI in 30 seconds (no AWS, no Docker)

A mock API with seeded demo links lets you click through everything: uploads, resume, passwords, limits, zip.

```bash
cd web && npm install
npm run mock     # terminal 1: mock API on :8080
npm run dev      # terminal 2: UI on http://localhost:5173
```

Demo links: `/d/demo1234abcd` (3 files), `/d/locked123456` (password `nitro`), `/d/expired00000` (expired). The mock
discards uploaded bytes and serves small zero-filled files, so it's for looking at the UI, not for testing
transfers.

## Run the whole stack locally (no AWS account)

moto (an S3 emulator with multipart support) stands in for S3, an nginx edge adds the CORS headers real S3 would,
and DynamoDB Local stands in for DynamoDB:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
```

Open http://localhost:8080. The access key is `dev-access-key-change-me`.

Without Docker, start an S3 emulator and DynamoDB Local yourself, then run the API with
`dotnet run --project api/src/NitroDrp.Api` (port 8080 via `ASPNETCORE_URLS`) and the UI with
`cd web && npm run dev`.

## Tests

| What | Command | Covers |
|---|---|---|
| API | `dotnet test api/NitroDrp.sln` | Share rules, limits, passwords, expiry, concurrency, resume, zip streaming, CloudFront signatures, the HTTP pipeline |
| Web | `cd web && npm test` | Chunk uploader, file matching, formatting, zip writer, the demo backend |
| Demo in a browser | `cd web && npm run build:demo && npx vite preview --port 4174` then `npm run e2e:demo` | Password gate, downloads, limits, footer, no requests to any server, an embedded iframe that grows and shrinks |
| Whole stack | `python3 dev/smoke.py [url]` | Against a running stack (local or deployed): 40 MB multipart upload with a simulated crash and resume, download, zip, byte-for-byte comparison, delete |
| Passkeys in a browser | `npm run e2e:passkeys` (needs the stack running; see the header of `web/e2e/passkeys.mjs`) | Enrolling, signing in on a second device, the shared Recent list, removing a device, cookie flags |
| Hostile input | `dotnet test api/NitroDrp.sln --filter HostileInputTests` | Garbage, oversized and malicious requests all get a clean 4xx (also part of the API suite) |
| Rate limit | `python3 dev/ratelimit-check.py https://your-site` | That forged client-IP headers can't dodge the limit (run against a deployed site) |
| Terraform | `cd infra/terraform && terraform init -backend=false && terraform test` | CloudFront on and off, email (SES) on and off, offline |
| Terraform state | `bash infra/test-state-backend.sh` | Bootstrap and migrate state against an S3 emulator, recovery, versioning, locking |
| `.env` script | `bash dev/test-setup-env.sh` | `deploy/setup-env.sh` in a scratch copy |
| Docs | `python3 dev/check-links.py` | Every relative link and anchor in the Markdown files |

CI runs all of these except the deployed-site checks (`.github/workflows/ci.yml`).

## Pinned dependencies

Builds are reproducible on purpose:

- **.NET:** every NuGet package has an exact version, and `api/**/packages.lock.json` also pins the transitive
  ones. The Dockerfile and CI restore with `--locked-mode`, so a build uses precisely these versions or fails.
  To upgrade deliberately: change the version, run `dotnet restore api/NitroDrp.sln --force-evaluate`, run the
  tests, and commit the lock files.
- **npm:** `package-lock.json` with `npm ci`; Playwright is pinned to an exact version.
- **Container images:** `cloudflared`, Caddy, moto and DynamoDB Local use exact tags in the Compose files. Bump
  them on purpose and re-run `python3 dev/smoke.py`.
- **Terraform:** `.terraform.lock.hcl` pins the AWS provider. On another OS or CPU, run
  `terraform providers lock -platform=linux_amd64 -platform=darwin_arm64 -platform=windows_amd64` once.
