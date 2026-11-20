variable "subscription_id" {
  description = "Azure subscription that hosts Photo Manager."
  type        = string
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "centralus"
}

variable "project" {
  description = "Short project name used in resource names."
  type        = string
  default     = "photo"

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,9}$", var.project))
    error_message = "project must be 2-10 lowercase letters/digits (it is part of globally unique names)."
  }
}

variable "environment" {
  description = "Environment name (part of resource names)."
  type        = string
  default     = "prod"
}

variable "name_suffix" {
  description = "Suffix for globally unique names (storage, registry). Empty derives a stable one from the subscription id."
  type        = string
  default     = ""
}

variable "github_repository" {
  description = "GitHub repository (owner/name) whose Actions may sign in to Azure with OIDC."
  type        = string
  default     = "biffkittz/photo-manager"
}

variable "state_resource_group_name" {
  description = "Resource group of the existing Terraform state storage account (must match the backend blocks)."
  type        = string
  default     = "rg-terraform-state"
}

variable "state_storage_account_name" {
  description = "Existing Terraform state storage account (must match the backend blocks)."
  type        = string
  default     = "demonstration"
}

variable "state_container_name" {
  description = "Blob container holding the state files (must match the backend blocks)."
  type        = string
  default     = "terraform"
}

variable "state_blob_prefix" {
  description = "Prefix of this project's state blobs in the shared container; CI identities can only touch these."
  type        = string
  default     = "photo."

  validation {
    condition     = can(regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,40}$", var.state_blob_prefix))
    error_message = "state_blob_prefix must be a short blob-name prefix (letters, digits, '.', '_', '-')."
  }
}

variable "scope_state_access_to_prefix" {
  description = "Limit the CI identities' state access to blobs named state_blob_prefix* with an ABAC condition. Set false only if the condition is rejected for the state account."
  type        = bool
  default     = true
}

variable "tags" {
  description = "Extra tags for every resource."
  type        = map(string)
  default     = {}
}
