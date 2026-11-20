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

variable "address_space" {
  description = "VNet address space (a /16 is carved into the subnets below)."
  type        = string
  default     = "10.40.0.0/16"
}

variable "tags" {
  description = "Tags."
  type        = map(string)
  default     = {}
}
