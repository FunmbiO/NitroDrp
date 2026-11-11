variable "region" {
  description = "AWS region for the bucket and table."
  type        = string
  default     = "ca-central-1"
}

variable "bucket_name" {
  description = "Globally unique S3 bucket name for uploaded files."
  type        = string
}

variable "table_name" {
  type    = string
  default = "nitrodrp"
}

variable "allowed_origins" {
  description = "Origins allowed to upload/download straight to S3 from the browser, e.g. [\"https://drop.example.com\"]."
  type        = list(string)
}

variable "expiry_days" {
  description = "Expiry choices offered in the UI. Each gets a lifecycle rule on prefix d<N>/."
  type        = list(number)
  default     = [1, 7, 30]
}

variable "monthly_budget_usd" {
  type    = number
  default = 10
}

variable "budget_email" {
  description = "Where AWS Budgets sends cost alerts."
  type        = string
}

variable "enable_cloudfront" {
  description = "Serve downloads through CloudFront. Set false to deploy S3-only (downloads come straight from S3, which bills egress after the first 100 GB/month), e.g. while AWS is still verifying the account for CloudFront. Flip to true and apply later."
  type        = bool
  default     = true
}

variable "cloudfront_public_key_pem" {
  description = "PEM public key whose private half the API uses to sign download URLs. Required when enable_cloudfront is true. Generate with: openssl genrsa -out cf-private.pem 2048 && openssl rsa -pubout -in cf-private.pem -out cf-public.pem"
  type        = string
  default     = ""
}

variable "cloudfront_price_class" {
  description = "PriceClass_100 = North America and Europe edges only (cheapest)."
  type        = string
  default     = "PriceClass_100"
}
