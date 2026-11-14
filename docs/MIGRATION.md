> **Note:** the move itself is done with plain AWS CLI commands in CloudShell (the Terraform in `infra/server` is untested). Only `infra/monitoring` is applied with Terraform. Run order and exact commands were given in the session; the steps below are the outline.

# Moving the server into Ready Set Build

Rule: **every AWS build lives in Ready Set Build (`659500704122`).** The Management Account (`062939323390`)
holds billing, the organization and cost alerts only. The Lightsail server (`nitrodrp`, us-east-2) started in the
management account, so it moves. Lightsail cannot move between accounts, so a new server is built and the old one is
retired.

One server runs two apps:

| App | Reached by | State to carry over |
|---|---|---|
| NitroDrp | `https://drop.jadedmedia.ca` (Caddy on 80/443) | None. Files are in S3, links in DynamoDB. Only the `.env`. |
| Opus | Tailscale only, no published ports | Two Docker volumes: `opus_data` (database) and `opus_keys` (master key). Plus `agent/.env`, `seed/`, `agent/style.md`. |

Opus holds email data. Never paste its files, keys or `.env` into chat or a ticket.

## 0. Before anything
Sign in to Ready Set Build (not the management account) for every step below.

```bash
cd infra/server
terraform init -backend-config=../terraform/backend.hcl -backend-config="key=nitrodrp/server.tfstate"
terraform plan   # check it says it will create 4 things in account 659500704122
terraform apply
```
The apply prints `static_ip`. Check the firewall came out right (80 and 443 open, 22 only for browser SSH):
```bash
aws lightsail get-instance --instance-name nitrodrp --region us-east-2 --query 'instance.networking.ports[].[fromPort,cidrs,cidrListAliases]' --output text
```
First boot installs Docker and swap by itself. Wait about 3 minutes, then **Connect using SSH** in the Lightsail
console (Ready Set Build account).

## 1. Back up Opus on the OLD server
```bash
cd ~/Opus && deploy/backup.sh ~/opus-backups     # stops Opus briefly
```
This writes `opus-data-*.tgz` and `opus-key-*.tgz`. Keep the two files apart.

## 2. Move NitroDrp first (it has no state)
1. New server: `git clone https://github.com/FunmbiO/NitroDrp.git && cd NitroDrp && ./deploy/setup-env.sh`
   Use the same bucket, table, region and **a new** access key for `nitrodrp-api` (IAM console, Ready Set Build).
2. `docker compose --profile public up -d --build`
3. Test it before switching DNS:
   `curl -k --resolve drop.jadedmedia.ca:443:<new-static-ip> https://drop.jadedmedia.ca/`
4. Lower the `drop` A record's TTL at Namecheap to 5 minutes a day ahead. Then change it to the new static IP.
5. Open the site, upload and download a small file, and send a test email link.

## 3. Move Opus
1. Copy the two backup files from the old server to the new one. Send them by different routes if you can
   (for example data by `scp`, key by a USB or password-manager attachment). Never through chat.
2. New server: `git clone https://github.com/FunmbiO/Opus.git && cd Opus`
3. Recreate the secrets by hand: `.env` (a **new single-use Tailscale auth key**), `agent/.env`, and your
   `seed/*.json` and `agent/style.md`. Values sealed with `npm run protect` do not travel: type the plain values.
4. `docker compose up -d --build` so the volumes exist, then restore (the commands in `docs/DEPLOY.md`):
   ```bash
   docker compose stop opus
   docker run --rm -v opus_data:/v -v "$PWD":/in alpine sh -c 'cd /v && tar xzf /in/opus-data-XXXX.tgz'
   docker run --rm -v opus_keys:/v -v "$PWD":/in alpine sh -c 'cd /v && tar xzf /in/opus-key-XXXX.tgz'
   docker compose start opus
   ```
5. On the old server run `docker compose down` in Opus. Remove the old `opus` machine in the Tailscale admin page.
6. Open Opus from your phone and laptop. Check the dashboard, a recent draft and the Gmail connection.

Opus is unreachable between steps 1 and 4 (minutes). If the key file is lost, the data cannot be read.

## 4. Retire the old server
Keep the old server running with both apps **stopped** for a few days. When the new one has been clean:
1. Lightsail, management account: delete the `nitrodrp` instance, then delete the `nitrodrp-ip` static IP.
2. IAM, Ready Set Build: delete the old `nitrodrp-api` access keys (the two not used by the new server).

## 5. Monitoring
```bash
cd infra/monitoring
terraform init -backend-config=../terraform/backend.hcl -backend-config="key=nitrodrp/monitoring.tfstate"
terraform apply -var alert_email=<your email>
```
Click the SNS confirmation link in your email. This adds the uptime alarm, CloudTrail and GuardDuty.
