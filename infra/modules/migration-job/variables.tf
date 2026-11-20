variable "name" {
  description = "Job name."
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

variable "environment_id" {
  description = "Container Apps environment id."
  type        = string
}

variable "identity_id" {
  description = "Migrator managed identity (PostgreSQL Entra administrator)."
  type        = string
}

variable "registry_server" {
  description = "Container registry login server."
  type        = string
}

variable "image" {
  description = "Initial image (CI/CD updates it before each run)."
  type        = string
}

variable "env" {
  description = "Environment variables."
  type        = map(string)
  default     = {}
}

variable "tags" {
  description = "Tags."
  type        = map(string)
  default     = {}
}
