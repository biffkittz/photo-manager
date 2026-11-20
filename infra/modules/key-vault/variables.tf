variable "name" {
  description = "Key Vault name (3-24 chars, globally unique)."
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

variable "private_endpoint_subnet_id" {
  description = "Subnet for the private endpoint."
  type        = string
}

variable "private_dns_zone_id" {
  description = "privatelink.vaultcore.azure.net zone id."
  type        = string
}

variable "admin_ip_ranges" {
  description = "Public IPs/CIDRs allowed through the firewall (operators setting secrets). Empty = private endpoint only."
  type        = list(string)
  default     = []
}

variable "admin_object_ids" {
  description = "Entra object ids that manage secrets and keys (Key Vault Secrets Officer + Crypto Officer)."
  type        = list(string)
  default     = []
}

variable "secret_reader_principal_ids" {
  description = "Managed identities that read secrets (Key Vault Secrets User), keyed by a static name."
  type        = map(string)
  default     = {}
}

variable "crypto_user_principal_ids" {
  description = "Managed identities that use keys for wrap/unwrap (Key Vault Crypto User), keyed by a static name."
  type        = map(string)
  default     = {}
}

variable "log_analytics_workspace_id" {
  description = "Workspace receiving the vault's audit log."
  type        = string
}

variable "tags" {
  description = "Tags."
  type        = map(string)
  default     = {}
}
