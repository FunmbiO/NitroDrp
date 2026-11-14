#!/usr/bin/env bash
# Runs once as root on first boot. Installs Docker and adds swap so image builds don't run out of memory.
# It does NOT start either app: secrets (.env files) are copied on by hand. See docs/MIGRATION.md.
set -euo pipefail

if [ ! -f /swapfile ]; then
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

apt-get update
DEBIAN_FRONTEND=noninteractive apt-get -y upgrade
curl -fsSL https://get.docker.com | sh
usermod -aG docker ubuntu

# Security fixes install themselves.
DEBIAN_FRONTEND=noninteractive apt-get -y install unattended-upgrades
