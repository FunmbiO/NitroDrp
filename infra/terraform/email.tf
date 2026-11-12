# --- Email (Amazon SES) -----------------------------------------------------------------------
# Optional: lets the app email download links. Off by default. Turning it on creates a verified
# sending domain with DKIM; you then add three DNS records (see the dkim_records output) and ask
# AWS to take the account out of the SES sandbox. docs/EMAIL.md walks through it.

resource "aws_sesv2_email_identity" "sender" {
  count          = var.enable_email ? 1 : 0
  email_identity = var.email_domain

  lifecycle {
    precondition {
      condition     = var.email_domain != ""
      error_message = "email_domain is required when enable_email is true (e.g. \"jadedmedia.ca\")."
    }
  }
}

locals {
  dkim_tokens = var.enable_email ? try(aws_sesv2_email_identity.sender[0].dkim_signing_attributes[0].tokens, []) : []
}
