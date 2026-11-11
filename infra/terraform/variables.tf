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
