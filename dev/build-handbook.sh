#!/usr/bin/env bash
# Rebuilds docs/NitroDrp-Handbook.pdf from docs/HANDBOOK.md. Needs pandoc, and `npm install` run once in web/
# (it borrows Playwright's Chromium). Run from anywhere:  bash dev/build-handbook.sh
set -euo pipefail
cd "$(dirname "$0")/.."
command -v pandoc >/dev/null || { echo "Install pandoc first (sudo apt install pandoc)." >&2; exit 1; }
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
pandoc docs/HANDBOOK.md -s --metadata pagetitle="NitroDrp Handbook" -c docs/handbook.css --embed-resources -o "$tmp/handbook.html"
node docs/handbook-pdf.mjs "$tmp/handbook.html" "$PWD/docs/NitroDrp-Handbook.pdf"
echo "Wrote docs/NitroDrp-Handbook.pdf"
