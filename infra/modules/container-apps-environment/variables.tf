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

variable "infrastructure_subnet_id" {
  description = "Delegated subnet (/23) for the environment."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Workspace receiving container logs."
  type        = string
}

variable "zone_redundancy_enabled" {
  description = "Spread replicas across availability zones."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags."
  type        = map(string)
  default     = {}
}
