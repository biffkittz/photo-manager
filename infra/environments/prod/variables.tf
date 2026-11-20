# ---- Azure ------------------------------------------------------------------------------------------
variable "subscription_id" {
  description = "Azure subscription id."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
  default     = "centralus"
}

variable "project" {
  description = "Short project name (must match infra/bootstrap)."
  type        = string
  default     = "photo"
}

variable "environment" {
  description = "Environment name (must match infra/bootstrap)."
  type        = string
  default     = "prod"
}

variable "name_suffix" {
  description = "Suffix for globally unique names (Key Vaults, storage account, PostgreSQL server); empty derives a stable one from the subscription id."
  type        = string
  default     = ""
}

variable "resource_group_name" {
  description = "App resource group created by infra/bootstrap; empty = rg-<project>-<environment>."
  type        = string
  default     = ""
}

variable "container_registry_name" {
  description = "Existing container registry holding the images (shared with another project)."
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

variable "image_tag" {
  description = "Image tag used when the Container Apps are first created (CI/CD deploys later tags)."
  type        = string
}

variable "vnet_address_space" {
  description = "VNet address space (/16)."
  type        = string
  default     = "10.40.0.0/16"
}

variable "admin_ip_ranges" {
  description = "Operator public IPs/CIDRs allowed to reach the Key Vaults (to set secrets). Empty = private endpoints only."
  type        = list(string)
  default     = []
}

variable "key_vault_admin_object_ids" {
  description = "Entra object ids allowed to manage secrets/keys (Key Vault Secrets Officer + Crypto Officer)."
  type        = list(string)
  default     = []
}

variable "postgres_admin_principals" {
  description = "Optional break-glass Entra administrators for PostgreSQL (the migrator identity is always one)."
  type = list(object({
    object_id      = string
    principal_name = string
    principal_type = string
  }))
  default = []
}

variable "postgres_version" {
  description = "PostgreSQL major version."
  type        = string
  default     = "17"
}

variable "postgres_sku" {
  description = "PostgreSQL SKU."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "log_retention_days" {
  description = "Log Analytics / Application Insights retention."
  type        = number
  default     = 30
}

# ---- Application -------------------------------------------------------------------------------------
variable "github_users" {
  description = "GitHub accounts allowed to sign in, by immutable numeric id (curl -s https://api.github.com/users/<login> | jq .id)."
  type = list(object({
    id    = number
    login = string
    role  = string
  }))

  validation {
    condition     = length(var.github_users) > 0 && alltrue([for u in var.github_users : contains(["Admin", "Member"], u.role) && u.id > 0])
    error_message = "github_users needs at least one entry; role must be Admin or Member and id a positive number."
  }
}

variable "web_min_replicas" {
  description = "Minimum web replicas. Use 0 for the very first apply (before Key Vault secrets and the database exist; see docs/deployment.md), then 1. 0 also allows scale to zero at the cost of cold starts."
  type        = number
  default     = 1

  validation {
    condition     = var.web_min_replicas >= 0 && var.web_min_replicas <= 10
    error_message = "web_min_replicas must be between 0 and 10."
  }
}

variable "web_max_replicas" {
  description = "Maximum web replicas."
  type        = number
  default     = 3
}

variable "worker_replicas" {
  description = "Worker replicas. Use 0 for the very first apply (before Key Vault secrets and the database exist; see docs/deployment.md), then 1. Temporal recommends two for always-on queues; one keeps cost minimal."
  type        = number
  default     = 1

  validation {
    condition     = var.worker_replicas >= 0 && var.worker_replicas <= 10
    error_message = "worker_replicas must be between 0 and 10."
  }
}

variable "wasabi_service_url" {
  description = "Wasabi S3 endpoint of the archive bucket."
  type        = string
  default     = "https://s3.us-east-2.wasabisys.com"
}

variable "wasabi_region" {
  description = "Wasabi signing region."
  type        = string
  default     = "us-east-2"
}

variable "wasabi_bucket" {
  description = "Archive bucket."
  type        = string
  default     = "photo-bk"
}

variable "gemini_model" {
  description = "Gemini model id (pinned)."
  type        = string
  default     = "gemini-3.8-flash"
}

variable "gemini_requests_per_second" {
  description = "Gemini calls per second across all worker replicas (Temporal task-queue rate limit)."
  type        = number
  default     = 1
}

# ---- DNS (Cloudflare) --------------------------------------------------------------------------------
variable "manage_dns" {
  description = "Create the Cloudflare records and the managed certificate for the custom domain."
  type        = bool
  default     = true
}

variable "custom_domain" {
  description = "Public hostname of the site."
  type        = string
  default     = "photo.biffkittz.com"
}

variable "cloudflare_zone_name" {
  description = "Cloudflare zone containing the hostname."
  type        = string
  default     = "biffkittz.com"
}

# ---- Temporal Cloud ----------------------------------------------------------------------------------
variable "manage_temporal_cloud" {
  description = "Manage the namespace and service accounts with the temporalcloud provider."
  type        = bool
  default     = true
}

variable "temporal_account_id" {
  description = "Temporal Cloud account id."
  type        = string
  default     = "joqqs"
}

variable "temporal_namespace_name" {
  description = "Namespace name without account suffix."
  type        = string
  default     = "photo"
}

variable "temporal_namespace_region" {
  description = "Namespace region with cloud prefix (e.g. aws-us-east-1). Must match the existing namespace."
  type        = string
  default     = ""

  validation {
    condition     = !var.manage_temporal_cloud || var.temporal_namespace_region != ""
    error_message = "temporal_namespace_region is required when manage_temporal_cloud is true."
  }
}

variable "temporal_import_existing_namespace" {
  description = "Import the existing namespace into state instead of creating it."
  type        = bool
  default     = true
}

variable "temporal_retention_days" {
  description = "Workflow history retention in days."
  type        = number
  default     = 30
}

variable "temporal_grpc_endpoint" {
  description = "Temporal Cloud gRPC endpoint used by the apps."
  type        = string
  default     = "photo.joqqs.tmprl.cloud:7233"
}

variable "temporal_api_key" {
  description = "Temporal Cloud API key for Terraform (account-level). Prefer TF_VAR_temporal_api_key or TEMPORAL_CLOUD_API_KEY."
  type        = string
  default     = null
  sensitive   = true
}

variable "tags" {
  description = "Extra tags."
  type        = map(string)
  default     = {}
}
