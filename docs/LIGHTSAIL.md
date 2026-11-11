# Hosting on AWS Lightsail (keeping your domain's DNS where it is)

Runs the app on a small always-on server with its own IP. Caddy gets and renews the HTTPS certificate by
itself, so there's no Cloudflare and no need to move your domain's DNS: you add **one A record** wherever
the domain's DNS lives (Namecheap, for `jadedmedia.ca`). Examples below use `send.jadedmedia.ca`.

Cost: roughly US$7-10 a month for the server (needs at least 1 GB of memory), plus about US$1-2 for S3 and
DynamoDB. Check current prices on the Lightsail pricing page.

You need first: the AWS stack from the README (`terraform apply`, S3-only mode is fine) and an access key
for the `nitrodrp-api` user.

## 1. Create the server (AWS console, Lightsail)
1. **Region: Ohio (`us-east-2`)**, the same region as your bucket. Some other US regions are blocked by
   organization policies on some accounts.
2. Linux/Unix, blueprint **OS Only -> Ubuntu 24.04 LTS**.
3. A plan with **IPv4** (not "IPv6-only": those can't have a static IP) and **at least 1 GB** of memory.
   Building the images needs the headroom; 2 GB is comfortable.
4. Name it `nitrodrp` and create it.
5. **Networking -> Create static IP**, attach it to the instance. Note the address. (It's free while attached.)
6. On the instance's **Networking** tab, keep **SSH (22)** and **HTTP (80)**, and add **HTTPS (443)**.
   Optional: add UDP 443 for HTTP/3, and restrict SSH to your own IP.

## 2. Point the subdomain at it (Namecheap)
**Domain List -> Manage -> Advanced DNS -> Add new record**: type **A Record**, host `send`, value = the static
IP, TTL Automatic. Leave every other record alone. Check from PowerShell:

```powershell
Resolve-DnsName send.jadedmedia.ca -Type A
```

Wait until it shows your IP before step 5, because Caddy needs it to prove you own the name. If the domain has
CAA records, one must allow `letsencrypt.org`.

## 3. Prepare the server
Open **Connect using SSH** on the instance (a browser terminal), then:

```bash
sudo apt update && sudo apt -y upgrade
```

If the plan has 1 GB of memory, add swap so image builds don't run out of memory:

```bash
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile && echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

```bash
curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker $USER
```

Close the SSH window and open a new one so the group change applies, then check `docker run --rm hello-world`.

## 4. Get the app and configure it
```bash
git clone https://github.com/FunmbiO/NitroDrp.git && cd NitroDrp && cp .env.example .env && chmod 600 .env
```

```bash
openssl rand -hex 32
```

Put that in `.env` as `API_KEY` (and save it in a password manager), then edit with `nano .env`:

| Variable | Value |
|---|---|
| `SENDER_NAME` | Jaded Media |
| `API_KEY` | the random value above |
| `AWS_REGION` | `us-east-2` |
| `S3_BUCKET`, `DYNAMO_TABLE` | from `terraform output` |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | a **new** access key for `nitrodrp-api` (IAM console), one per server |
| `SITE_ADDRESS` | `send.jadedmedia.ca` |

Leave the `CLOUDFRONT_*` and `TUNNEL_TOKEN` values empty.

## 5. Allow the site in the bucket's CORS rules
On your own computer, add the hostname to `allowed_origins` in `infra/terraform/terraform.tfvars` and apply.
While you're there, raise the budget alert, since the server alone is close to the default US$10:

```hcl
allowed_origins    = ["https://send.jadedmedia.ca", "http://localhost:8080"]
monthly_budget_usd = 20
```

```bash
cd infra/terraform && terraform apply
```

## 6. Start it
Back on the server:

```bash
docker compose --profile public up -d --build
```

The first build takes a few minutes on a small server.

```bash
docker compose logs caddy
```

Look for "certificate obtained successfully".

## 7. Verify
- `https://send.jadedmedia.ca/api/health` returns `{"status":"ok"}`.
- From your computer (needs Python 3): the full upload/resume/download/zip/delete check, then the rate-limit check.

```bash
API_KEY=your-api-key python3 dev/smoke.py https://send.jadedmedia.ca
```

```bash
python3 dev/ratelimit-check.py https://send.jadedmedia.ca
```

The first should print twelve PASS lines. The second should print PASS: the server overwrites forged
client-IP headers, so a visitor can't dodge the password-guess limit.

## 8. Afterwards
- Stop any copy of the stack running on your PC, and deactivate the PC's AWS access key if you made one for it.
- Updating: `cd ~/NitroDrp && git pull && docker compose --profile public up -d --build`.
- After a reboot the containers come back on their own.
- Nothing on the server needs backing up except `.env`: keep a copy in a password manager.
- When AWS verifies your account for CloudFront, see "Deploying without CloudFront" in the README to switch it on.

## If something breaks
| Symptom | Likely cause |
|---|---|
| No certificate / browser warning | DNS doesn't point at the static IP yet, or ports 80/443 are closed in the Lightsail firewall. Check `docker compose logs caddy`. Fix it, then `docker compose restart caddy`. |
| 502 Bad Gateway | The API isn't running. `docker compose logs api` (usually a missing value in `.env`). |
| Build killed or "out of memory" | Add the swap file from step 3, or move to the next plan size. |
| Uploads fail with a CORS error | `allowed_origins` doesn't exactly match `https://send.jadedmedia.ca`. Re-run `terraform apply`. |
| `AccessDenied` in the API log | The AWS access key in `.env` is wrong or inactive. |
