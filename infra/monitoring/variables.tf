variable "aws_account_id" {
  description = "The only account Terraform may touch: Ready Set Build."
  type        = string
  default     = "659500704122"
}

variable "region" {
  type    = string
  default = "us-east-2"
}

variable "alert_email" {
  description = "Where downtime alerts go."
  type        = string
}

variable "site_host" {
  description = "Hostname the uptime check calls over HTTPS."
  type        = string
  default     = "drop.jadedmedia.ca"
}
