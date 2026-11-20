variable "name" {
  description = "Server name (globally unique)."
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

variable "tenant_id" {
  description = "Microsoft Entra tenant id."
  type        = string
}

variable "delegated_subnet_id" {
  description = "Delegated subnet (VNet integration)."
  type        = string
}

variable "private_dns_zone_id" {
  description = "Private DNS zone for the server."
  type        = string
}

variable "postgres_version" {
  description = "PostgreSQL major version."
  type        = string
  default     = "17"
}

variable "sku_name" {
  description = "SKU (B_Standard_B1ms is the smallest burstable tier)."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "storage_mb" {
  description = "Storage size in MB."
  type        = number
  default     = 32768
}

variable "backup_retention_days" {
  description = "Point-in-time restore window (7-35 days)."
  type        = number
  default     = 14
}

variable "database_name" {
  description = "Application database."
  type        = string
  default     = "photomanager"
}

variable "entra_administrators" {
  description = "Microsoft Entra administrators (the migrator identity, plus optional break-glass users/groups), keyed by a static name."
  type = map(object({
    object_id      = string
    principal_name = string
    principal_type = string # ServicePrincipal | User | Group
  }))
}

variable "log_analytics_workspace_id" {
  description = "Workspace receiving server logs."
  type        = string
}

variable "tags" {
  description = "Tags."
  type        = map(string)
  default     = {}
}
