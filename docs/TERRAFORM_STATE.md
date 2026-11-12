# Terraform state in S3

Terraform records what it built in a "state" file. Kept only on one computer, losing that computer means
Terraform forgets your infrastructure. This keeps it in a private, versioned S3 bucket instead, with locking so
two runs can't collide. Cost: pennies a month.

## Move your existing state (one time)

On the computer where you ran `terraform apply`, in the Ubuntu terminal, with the same AWS credentials
(`terraform-admin`) that `aws sts get-caller-identity` shows:

1. **Get the latest code.**

```bash
cd ~/NitroDrp && git checkout main && git pull
```

2. **Take a manual backup first.** Belt and braces, in case anything goes wrong:

```bash
cp infra/terraform/terraform.tfstate ~/tfstate-backup-$(date +%F).tfstate
```

3. **Create the state bucket.** It makes `nitrodrp-tfstate-<your account id>` in `us-east-2`, private, versioned
   and encrypted, and writes `infra/terraform/backend.hcl` for you:

```bash
./infra/bootstrap/state-bucket.sh
```

4. **Move the state into it.** Answer `yes` when Terraform asks to copy the existing state:

```bash
cd infra/terraform && terraform init -migrate-state -backend-config=backend.hcl
```

5. **Check nothing changed.** This should say no changes, or show only changes you meant to make:

```bash
terraform plan
```

6. **Confirm it's in S3:**

```bash
aws s3 ls s3://nitrodrp-tfstate-$(aws sts get-caller-identity --query Account --output text)/nitrodrp/
```

You should see `terraform.tfstate`. Keep the manual backup from step 2 for a while, then delete it.

Your `terraform.tfvars` file is **not** part of the state and stays on your computer. It holds no secrets (only
names and the public key), but keep a copy somewhere, since Terraform needs it to run.

## Using it from another computer

Install Terraform and the AWS CLI, sign in as `terraform-admin`, clone the repo, then:

```bash
./infra/bootstrap/state-bucket.sh          # only rewrites backend.hcl and re-applies the settings
cd infra/terraform && terraform init -backend-config=backend.hcl
```

and add your `terraform.tfvars`. Terraform pulls the state down by itself.

## If something goes wrong

- **"Error acquiring the state lock":** another run is in progress, or one crashed. Wait, or if you're sure
  nothing is running, `terraform force-unlock <the lock id it printed>`.
- **A bad apply damaged the state:** the bucket keeps every version for 90 days. List them and restore one:

```bash
aws s3api list-object-versions --bucket YOUR-STATE-BUCKET --prefix nitrodrp/terraform.tfstate
```

```bash
aws s3api get-object --bucket YOUR-STATE-BUCKET --key nitrodrp/terraform.tfstate --version-id VERSION-ID restored.tfstate
```

  then check `restored.tfstate` and put it back with `terraform state push restored.tfstate`.
- **Go back to a local file:** delete `infra/terraform/backend.tf`, then `terraform init -migrate-state`.

## Why a separate bucket

Not the files bucket: the app's AWS user can read and write everything in that one, and its lifecycle rules
delete objects. State needs neither. The bucket refuses plain-HTTP requests, blocks all public access, and the
state is encrypted at rest.

## How it's tested

`infra/test-state-backend.sh` runs the bootstrap script and a full migration against an S3 emulator (also in
CI): migrate local state, delete every local file and recover from S3, no changes planned, old versions kept,
and a second simultaneous run refused by the lock.
