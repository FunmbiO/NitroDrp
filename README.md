# NitroDrp

```
$ nitrodrp send --private
```

A private, self-hosted file transfer app: upload big files, get an unguessable link, set an expiry, an optional
password and a download limit. Built to replace a WeTransfer subscription, and as a portfolio project.

**Live demo: [demo.jadedmedia.ca](https://demo.jadedmedia.ca)**. The whole app, with the backend running in your
browser, so nothing is uploaded. ([How the demo works](docs/DEMO.md))

| Send | Link ready |
|---|---|
| ![Upload screen: drop files, choose expiry, password and download limit](docs/screenshots/1-upload.png) | ![Link created, with a summary of its settings](docs/screenshots/2-link-created.png) |
| **Receive** | **On a phone** |
| ![Recipient page with download-all and per-file buttons](docs/screenshots/3-download.png) | ![The recipient page on a phone](docs/screenshots/4-mobile.png) |
| **Email the link (optional)** | |
| ![Optional email section on the upload screen](docs/screenshots/5-email.png) | |

## Highlights

- **Multi-GB uploads that survive bad connections.** The browser sends 16 MiB parts straight to S3 (the server only
  signs URLs), retries each part, and **resumes after a reload**.
- **Links that expire on their own.** The API refuses expired links at once; DynamoDB TTL and S3 lifecycle rules
  delete the data. No cleanup job.
- **Optional password and download limit**, with the limit enforced by one conditional write, so concurrent
  downloads can't exceed it.
- **"Download all" as a streamed .zip** (ZIP64, flat memory), verified with a 4.8 GB archive.
- **Send the link by email** (optional, Amazon SES): type recipients on the upload page or later, with a note.
  The sender gets the replies, the password is never emailed, and each link has a lifetime cap.
- **Installs on a phone** as a home-screen app (a PWA): full screen, its own icon, opens offline.
- **Optional CloudFront** for downloads, with signed URLs; works fine without it.
- **One app on every device:** sign in with Face ID or a fingerprint (passkeys), and see the same links on your phone
  and laptop ([details](docs/AUTH.md)).
- **Private by design:** a shared access key to create links, a per-share owner key, PBKDF2 passwords, least-privilege
  AWS access, and rate limits that forged headers can't dodge.
- **Infrastructure as code:** Terraform for everything on AWS, with state in its own versioned bucket and offline
  tests; Docker Compose for the app; reproducible, pinned builds; CI for the API, web, demo, scripts and Terraform.

**Stack:** ASP.NET Core 10 (minimal APIs) · React + TypeScript (Vite) · Amazon S3 · DynamoDB · SES (optional) · CloudFront
(optional) · Terraform · Docker Compose · Caddy or Cloudflare Tunnel · AWS Lightsail.

## Docs

| | |
|---|---|
| [Architecture](docs/ARCHITECTURE.md) | Diagram, design decisions, security model and the API |
| [Development](docs/DEVELOPMENT.md) | Repo map, running it locally, every test suite, pinned dependencies |
| [Deploying](docs/DEPLOY.md) | AWS and Terraform, `.env`, the three ways to expose it, CloudFront |
| [Lightsail guide](docs/LIGHTSAIL.md) | Step by step: a small always-on server with automatic HTTPS |
| [Terraform state](docs/TERRAFORM_STATE.md) | Keeping state in S3, moving to it, recovering |
| [Passkeys](docs/AUTH.md) | Signing in with Face ID or a fingerprint, one app across devices, removing a device |
| [Phone app](docs/PHONE.md) | Installing NitroDrp on a phone's home screen and what to expect |
| [Email](docs/EMAIL.md) | Emailing links with Amazon SES: setup, DKIM records, leaving the sandbox, limits |
| [The demo](docs/DEMO.md) | The in-browser demo, deploying it on Vercel, embedding it |

## Try it

```bash
cd web && npm install
npm run mock     # terminal 1: a mock API with demo links
npm run dev      # terminal 2: the UI on http://localhost:5173
```

or the whole stack locally with S3 and DynamoDB emulators, no AWS account:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
python3 dev/smoke.py        # in another terminal: checks upload, resume, download, zip and delete end to end
```

Open http://localhost:8080 with the access key `dev-access-key-change-me`.

## Status

Deployed and running: the API on a Lightsail server behind Caddy, S3 and DynamoDB, Terraform with remote state,
S3-only downloads. Tested by 138 API tests, 45 web tests, a browser test of the demo, Terraform and script tests,
and an end-to-end smoke test against the live site.

Not yet exercised against real services: emailing links (it needs the SES setup in [docs/EMAIL.md](docs/EMAIL.md)), the
CloudFront path (AWS is verifying the account) and the Cloudflare Tunnel option.

Next: zips are streamed through the server, so heavy use would want a Lambda that zips next to S3.

## Branding

Every page ends with a credit line: the Jaded Media monogram and "Powered by jadedmedia.ca, used for personal and
customer files". To rebrand your own instance, change the text and link in `web/src/components/Footer.tsx` and
replace `web/src/assets/jadedmedia-logo.png` (a white logo on a transparent background suits the dark theme).

## License

The code is [MIT](LICENSE). The Jaded Media logo and name are **not** covered by it ([details](web/src/assets/NOTICE.md)). Third-party software and its licenses: [THIRD_PARTY.md](THIRD_PARTY.md).
