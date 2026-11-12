# Deploying

The backend is always AWS (S3, DynamoDB, an IAM user, an optional CloudFront distribution), created with Terraform.
What differs is how the app is exposed to the internet. Pick one:

| Way | Best for | Notes |
|---|---|---|
| **AWS Lightsail server + Caddy** | Always-on, client-facing use | Automatic HTTPS, and your domain's DNS stays wherever it is: [LIGHTSAIL.md](LIGHTSAIL.md) |
| **Cloudflare Tunnel** | A home server or a PC | Needs the domain's DNS on Cloudflare; no open ports |
| **Local only** | Trying it out | `http://localhost:8080`, only you can reach it |

How it fits together: [ARCHITECTURE.md](ARCHITECTURE.md). To try it with no AWS at all, see
[DEVELOPMENT.md](DEVELOPMENT.md).

## 1. AWS and Terraform

You need the AWS CLI, Terraform 1.10 or newer, and an AWS user for running Terraform (an administrator, not the
root user's keys).

1. **Create the state bucket.** Terraform's record of what it built lives in its own private S3 bucket, not on
   one computer. Create it once; it also writes the `backend.hcl` Terraform needs. Already applied with local
   state? [TERRAFORM_STATE.md](TERRAFORM_STATE.md) moves it.

   ```bash
   ./infra/bootstrap/state-bucket.sh
   ```

2. **Fill in your settings.**

   ```bash
   cd infra/terraform && cp terraform.tfvars.example terraform.tfvars
   ```

   Edit `terraform.tfvars`:
   - `bucket_name`: globally unique.
   - `region`: where the bucket and table go. Some AWS Organizations block regions; if creating the bucket is
     refused, try another.
   - `allowed_origins`: the exact `https://` hostname people will use, plus `http://localhost:8080` to test
     locally. If it doesn't match exactly, browser uploads fail with a CORS error.
   - `budget_email` and `monthly_budget_usd`: where cost alerts go, and the limit.
   - `enable_cloudfront`: see [CloudFront](#cloudfront-optional) below. `false` works fine to start.

3. **Apply.** This creates the private bucket (CORS and expiry rules), the DynamoDB table (TTL), a least-privilege
   IAM user and the budget alert.

   ```bash
   terraform init -backend-config=backend.hcl && terraform apply
   ```

4. **Create the app's access key.** In the IAM console: Users, `nitrodrp-api`, Security credentials, Create access
   key, "Application running outside AWS". Use the copy buttons for both values; the secret is shown only once.

Terraform has offline tests (`terraform init -backend=false && terraform test`) that check both CloudFront modes
without an AWS account.

## 2. The app's settings

On the machine that will run the app, `./deploy/setup-env.sh` writes `.env` for you by asking for each value
(the secret is typed hidden), generates the `API_KEY`, and keeps the file private. **It prints the `API_KEY` once:
save it in a password manager.** That's the access key the upload page asks for.

## 3. Expose it

**Lightsail with Caddy:** follow [LIGHTSAIL.md](LIGHTSAIL.md), which includes the server, the DNS record and
`docker compose --profile public up -d --build`.

**Cloudflare Tunnel:** in Cloudflare Zero Trust, Networks, Tunnels, create a tunnel; add a public hostname that
points at `http://web:80`; put the tunnel's token in `.env` as `TUNNEL_TOKEN`. Then:

```bash
docker compose --profile tunnel up -d --build
```

**Local only:**

```bash
docker compose up -d --build
```

Then open `http://localhost:8080` (and keep that origin in `allowed_origins`).

## 4. Check it

```bash
API_KEY=your-api-key python3 dev/smoke.py https://your-site
python3 dev/ratelimit-check.py https://your-site
```

The first prints twelve PASS lines: S3, DynamoDB, resume, download, zip and delete all work. The second proves a
visitor can't dodge the password-guess limit with forged headers.

## CloudFront (optional)

CloudFront serves downloads from the edge for free transfer; without it, downloads come straight from S3, which
bills internet egress (about US$0.09 per GB after the first 100 GB a month, so nothing at light use).

AWS sometimes holds back new CloudFront distributions until the account is verified by Support (the error reads
"Your account must be verified before you can add new CloudFront resources"). You don't have to wait: set
`enable_cloudfront = false`, apply, and leave the three `CLOUDFRONT_*` values empty in `.env`. Everything works.

To turn it on, when you're ready:

1. Make the key that signs download URLs. The private key never leaves your server:

   ```bash
   mkdir -p secrets
   openssl genrsa -out secrets/cloudfront-private.pem 2048
   openssl rsa -pubout -in secrets/cloudfront-private.pem -out cf-public.pem
   ```

2. In `terraform.tfvars` set `enable_cloudfront = true` and paste the contents of `cf-public.pem` into
   `cloudfront_public_key_pem`, then `terraform apply`. CloudFront takes a few minutes to deploy.
3. Put `CLOUDFRONT_DOMAIN`, `CLOUDFRONT_KEY_PAIR_ID` and `CLOUDFRONT_DISTRIBUTION_ID` (from `terraform output`) in
   `.env`, and restart.

If you applied an earlier version that already created some CloudFront pieces, `moved` blocks adopt them instead
of recreating them; with the switch off, Terraform plans to remove those four free pieces (key, key group, access
control, cache policy), which is expected.

## Operating it

- **Updating:** `git pull` and run the same `docker compose --profile <public|tunnel> up -d --build` you started
  with. Containers restart on their own after a reboot.
- **Ports:** the web container (and the dev S3 edge) publish only on `127.0.0.1`. The tunnel or Caddy reaches
  nginx over Docker's internal network, so nothing needs your LAN to see it. That matters because the API
  rate-limits on a header the proxy in front sets, which anyone who could reach the port directly could forge.
- **Embedding the real app** in another site: only origins you list in `FRAME_ANCESTORS` may (default: none). You'd
  normally embed the [demo](DEMO.md) instead.
- **Cost:** with a small Lightsail server expect roughly US$8 to 12 a month all in, mostly the server. Check
  current prices on AWS's pricing pages.
