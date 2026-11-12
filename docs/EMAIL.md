# Emailing download links

Optional. When it is on, the sender can type one or more email addresses on the upload page ("Also send the link
by email"), or press **Email this link** on the link-ready page later. They can still just copy the link. Email
is sent by Amazon SES, so it stays on AWS, and it costs about US$0.10 per 1,000 emails.

What a recipient gets: a short message from **your brand** (`SENDER_NAME`) with the link, the file count and size,
the expiry date, and the sender's note. **Replies go to the sender's own address**, because the mail is sent from
your domain but carries the sender's address as Reply-To.

## What it will and won't do

- **The password is never emailed.** If a link has a password, the email says so and tells the recipient to ask
  the sender for it. Password-protected links also hide their file names in the email.
- **Only the person who made the link can email it** (the same owner key that lets them delete it). An access-key
  holder can't email someone else's link, and a link has to be finished (live and unexpired) first.
- **Limits.** 10 addresses per send, and 20 emails in total per link, counted by one atomic write so repeated or
  parallel clicks can't exceed it. Both are settings (`NitroDrp:Email:MaxRecipientsPerRequest` / `MaxRecipientsPerShare`). Each
  IP is also rate limited. This is what stops the feature being used to spam strangers.
- **Only plain addresses are accepted** (`name@domain.tld`): no `Name <a@b>`, no lists, no line breaks, so a
  recipient field can't be used to inject headers or extra recipients.
- **One recipient's failure doesn't stop the rest.** The page lists each address as Sent or Not sent; failed
  addresses stay in the box so they can be corrected and retried.
- Recipient addresses are never written to the server's logs.

## Set it up

You need a domain you control, and DNS access to it (for Namecheap: Domain List, Manage, Advanced DNS).

### 1. Create the sending identity with Terraform

In `infra/terraform/terraform.tfvars`:

```hcl
enable_email = true
email_domain = "jadedmedia.ca"
```

```bash
cd infra/terraform
terraform apply
terraform output email_dkim_records
```

This creates a verified-domain identity in SES, and lets the API's IAM user send mail **only from addresses at
that domain** (`ses:FromAddress` condition). It sends from the same region as the rest of the stack, so the SES
region must be one your AWS organization allows.

### 2. Add the three DKIM records

`email_dkim_records` prints three CNAME records. At your DNS host, add each one: the **Host** is the part before
`.jadedmedia.ca` (for example `abc123._domainkey`), the **Value** is the `...dkim.amazonses.com` target. Namecheap
appends the domain itself, so don't type it twice. Within minutes to an hour, the identity in the SES console
(Configuration, Identities) turns **Verified**.

### 3. Leave the SES sandbox

A new SES account is in a **sandbox**: it can only send to addresses you have verified one by one. That is fine for
testing with your own addresses. To email anyone, open the SES console, **Account dashboard**, **Request production
access**, and say what it is: a private file-transfer tool that emails download links that the sender asked for.
Approval usually takes about a day. Until then, mail to unverified addresses is refused and the page reports
those recipients as "Not sent".

### 4. Turn it on in the app

Run `./deploy/setup-env.sh` and answer **y** to the email question, or add these to `.env` by hand:

```
EMAIL_ENABLED=true
EMAIL_FROM_ADDRESS=transfers@jadedmedia.ca
PUBLIC_BASE_URL=https://drop.jadedmedia.ca
```

Then `docker compose --profile public up -d --build`. The API refuses to start if email is on but the from
address or `PUBLIC_BASE_URL` is missing, so a typo can't silently break it. With email off, the upload page doesn't
show any email options at all.

### 5. Try it

Make a link, tick **Also send the link by email**, enter your own address. It should arrive within seconds. If it
doesn't, look in spam, then check that the identity shows Verified and, while still in the sandbox, that the
recipient is a verified address.

## Locally and in the demo

- `docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build` runs email against moto's SES
  emulator, with the sending domain auto-created. `python3 dev/smoke.py` then emails a link and checks the
  results and limits. To also confirm what the emulator recorded, set
  `MOTO_DATA_URL=http://localhost:5000/moto-api/data.json` (publish moto's port first).
- The browser demo at demo.jadedmedia.ca **simulates** it with the same rules and error codes, and says so on the
  page. No email is ever sent from the demo.

## Configuration reference

| Setting (.env) | App setting | Default |
|---|---|---|
| `EMAIL_ENABLED` | `NitroDrp:Email:Enabled` | `false` |
| `EMAIL_FROM_ADDRESS` | `NitroDrp:Email:FromAddress` | none (required when on) |
| `PUBLIC_BASE_URL` | `NitroDrp:PublicBaseUrl` | none (required when on) |
| `EMAIL_REGION` | `NitroDrp:Email:Region` | `AWS_REGION` |
| | `NitroDrp:Email:MaxRecipientsPerRequest` | 10 |
| | `NitroDrp:Email:MaxRecipientsPerShare` | 20 |
| | `NitroDrp:Email:ConfigurationSet` | none (optional SES configuration set) |
