variable "name" {
  description = "Base name, e.g. photo-prod."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "retention_in_days" {
  description = "Log retention."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Tags."
  type        = map(string)
  default     = {}
}
