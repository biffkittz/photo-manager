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

variable "github_repository" {
  description = "GitHub repository (owner/name) whose Actions may sign in to Azure with OIDC."
  type        = string
  default     = "biffkittz/photo-manager"
}

variable "container_registry_name" {
  description = "Existing container registry the images are pushed to (shared with another project)."
  type        = string
  default     = "crloginappdemo7fyguj"

  validation {
    condition     = can(regex("^[a-zA-Z0-9]{5,50}$", var.container_registry_name))
    error_message = "container_registry_name must be 5-50 letters or digits (an Azure Container Registry name)."
  }
}

variable "container_registry_resource_group" {
  description = "Resource group of the existing container registry (same subscription)."
  type        = string
  default     = "rg-loginapp-demo"
}

variable "container_registry_login_server" {
  description = "Login server of the registry; empty means <name>.azurecr.io. Set it only if the registry uses a domain name label (login server with a hash suffix)."
  type        = string
  default     = ""
}

variable "registry_cleanup_enabled" {
  description = "Run the weekly clean-up of this project's images (photo-web, photo-worker, photo-migrator) in the shared registry."
  type        = bool
  default     = true
}

variable "registry_cleanup_older_than_days" {
  description = "Image tags older than this many days are candidates for deletion."
  type        = number
  default     = 30

  validation {
    condition     = var.registry_cleanup_older_than_days >= 7
    error_message = "registry_cleanup_older_than_days must be at least 7."
  }
}

variable "registry_cleanup_keep" {
  description = "Always keep this many of the newest candidate tags per repository (rollback targets)."
  type        = number
  default     = 10

  validation {
    condition     = var.registry_cleanup_keep >= 3
    error_message = "registry_cleanup_keep must be at least 3 so recent releases stay available for rollback."
  }
}

variable "registry_cleanup_dry_run" {
  description = "Only log what the clean-up would delete (az acr task logs); nothing is removed."
  type        = bool
  default     = false
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
