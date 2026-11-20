variable "namespace_name" {
  description = "Namespace name without the account suffix (photo for photo.joqqs)."
  type        = string
}

variable "region" {
  description = "Namespace region with cloud prefix, e.g. aws-us-east-1 (must match an existing namespace; regions cannot change)."
  type        = string
}

variable "retention_days" {
  description = "Workflow history retention (1-90 days)."
  type        = number
  default     = 30
}

variable "service_account_prefix" {
  description = "Prefix for the per-tier service accounts."
  type        = string
}
