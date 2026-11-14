terraform {
  required_version = ">= 1.10" # S3-native state locking (use_lockfile)
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

provider "aws" {
  region = var.region

  # Refuse to run against any account but Ready Set Build (never the management account).
  allowed_account_ids = [var.aws_account_id]

  # The AWS console tags an IAM user with each access key's description, using the key id as the tag name
  # (AKIA...). Terraform doesn't manage those, so without this every plan tries to remove them.
  ignore_tags {
    key_prefixes = ["AKIA"]
  }
}
