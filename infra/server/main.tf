# The app server (NitroDrp + Opus) on Lightsail, in the Ready Set Build account.
# Separate from ../terraform so the app stack and its offline tests never depend on it.
#
#   terraform init -backend-config=../terraform/backend.hcl -backend-config="key=nitrodrp/server.tfstate"
#   terraform apply

terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
  backend "s3" {}
}

provider "aws" {
  region = var.region
  # Refuse to run against any other account (for example the management account).
  allowed_account_ids = [var.aws_account_id]
}

resource "aws_lightsail_instance" "server" {
  name              = var.name
  availability_zone = "${var.region}a"
  blueprint_id      = "ubuntu_24_04"
  bundle_id         = var.bundle_id
  ip_address_type   = "dualstack"
  user_data         = file("${path.module}/cloud-init.sh")
}

resource "aws_lightsail_static_ip" "server" {
  name = "${var.name}-ip"
}

resource "aws_lightsail_static_ip_attachment" "server" {
  static_ip_name = aws_lightsail_static_ip.server.name
  instance_name  = aws_lightsail_instance.server.name
}

# Web ports are public (Caddy for NitroDrp). SSH is only reachable from Lightsail's browser SSH button
# (and any CIDRs you list in ssh_cidrs). Opus publishes no ports at all: it is reached over Tailscale.
# After the first apply, check the result: aws lightsail get-instance --instance-name <name> --query instance.networking.ports
resource "aws_lightsail_instance_public_ports" "server" {
  instance_name = aws_lightsail_instance.server.name

  port_info {
    protocol  = "tcp"
    from_port = 80
    to_port   = 80
  }
  port_info {
    protocol  = "tcp"
    from_port = 443
    to_port   = 443
  }
  port_info {
    protocol          = "tcp"
    from_port         = 22
    to_port           = 22
    cidrs             = var.ssh_cidrs
    cidr_list_aliases = ["lightsail-connect"]
  }
}

output "static_ip" {
  description = "Point the drop.jadedmedia.ca A record here (Namecheap) when you cut over."
  value       = aws_lightsail_static_ip.server.ip_address
}
