variable "name" {
  description = "Storage account name (3-24 lowercase letters/digits, globally unique)."
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

variable "replication_type" {
  description = "Replication (renditions can be regenerated from the archive; staging/downloads are transient)."
  type        = string
  default     = "LRS"
}

variable "containers" {
  description = "Private blob containers to create."
  type        = list(string)
  default     = ["staging", "renditions", "downloads", "dataprotection"]
}

variable "expiring_containers" {
  description = "Containers whose blobs are deleted one day after their last modification."
  type        = list(string)
  default     = ["staging", "downloads"]
}

variable "private_endpoint_subnet_id" {
  description = "Subnet for the blob private endpoint."
  type        = string
}

variable "private_dns_zone_id" {
  description = "privatelink.blob.core.windows.net zone id."
  type        = string
}

variable "container_role_assignments" {
  description = "Container-scoped data-plane roles, keyed by a static name."
  type = map(object({
    container    = string
    principal_id = string
    role         = string
  }))
  default = {}
}

variable "tags" {
  description = "Tags."
  type        = map(string)
  default     = {}
}
