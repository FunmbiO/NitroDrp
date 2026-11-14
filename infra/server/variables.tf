variable "aws_account_id" {
  description = "The only account Terraform may touch: Ready Set Build."
  type        = string
  default     = "659500704122"
}

variable "region" {
  type    = string
  default = "us-east-2"
}

variable "name" {
  type    = string
  default = "nitrodrp"
}

variable "bundle_id" {
  description = "Lightsail plan. small_3_0 is 2 GB and runs both apps with room to build images."
  type        = string
  default     = "small_3_0"
}

variable "ssh_cidrs" {
  description = "Extra CIDRs allowed to SSH in, besides Lightsail's browser SSH. Leave empty."
  type        = list(string)
  default     = []
}
