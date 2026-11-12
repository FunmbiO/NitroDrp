# Offline tests for the optional email (SES) resources.

# The policy JSON is only known at plan time when every ARN in it is, so the mock supplies them.
mock_provider "aws" {
  mock_data "aws_iam_policy_document" {
    defaults = { json = "{\"Version\":\"2012-10-17\",\"Statement\":[]}" }
  }
  mock_resource "aws_s3_bucket" {
    defaults = { arn = "arn:aws:s3:::test-nitrodrp-files" }
  }
  mock_resource "aws_dynamodb_table" {
    defaults = { arn = "arn:aws:dynamodb:us-east-2:111111111111:table/nitrodrp" }
  }
}

variables {
  bucket_name       = "test-nitrodrp-files"
  allowed_origins   = ["https://drop.example.com"]
  budget_email      = "you@example.com"
  enable_cloudfront = false
}

run "email_is_off_by_default" {
  command = plan

  assert {
    condition     = length(aws_sesv2_email_identity.sender) == 0
    error_message = "No SES identity unless enable_email is true."
  }

  assert {
    condition     = !anytrue([for st in data.aws_iam_policy_document.api.statement : contains(st.actions, "ses:SendEmail")])
    error_message = "The API user must not be able to send mail when email is off."
  }
}

run "email_on_creates_identity_and_scoped_permission" {
  command = plan

  variables {
    enable_email = true
    email_domain = "example.com"
  }

  assert {
    condition     = length(aws_sesv2_email_identity.sender) == 1 && aws_sesv2_email_identity.sender[0].email_identity == "example.com"
    error_message = "Email on must create one identity for the domain."
  }

  assert {
    condition     = anytrue([for st in data.aws_iam_policy_document.api.statement : contains(st.actions, "ses:SendEmail")]) && contains(flatten([for st in data.aws_iam_policy_document.api.statement : [for c in st.condition : c.variable if contains(st.actions, "ses:SendEmail")]]), "ses:FromAddress") && contains(flatten([for st in data.aws_iam_policy_document.api.statement : [for c in st.condition : tolist(c.values) if contains(st.actions, "ses:SendEmail")]]), "*@example.com")
    error_message = "Sending must be limited to addresses at the domain."
  }
}

run "email_on_needs_a_domain" {
  command = plan

  variables {
    enable_email = true
    email_domain = ""
  }

  expect_failures = [aws_sesv2_email_identity.sender]
}

# The DKIM tokens only exist once SES creates the identity, and the mock provider cannot fake that nested
# value, so the record format is checked by hand in docs/EMAIL.md instead of here.
run "email_off_publishes_no_records" {
  command = apply

  assert {
    condition     = length(output.email_dkim_records) == 0 && output.email_identity_arn == null
    error_message = "With email off there is nothing to add to DNS."
  }
}
