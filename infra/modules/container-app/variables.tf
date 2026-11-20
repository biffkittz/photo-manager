variable "name" {
  description = "Container App name."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group."
  type        = string
}

variable "environment_id" {
  description = "Container Apps environment id."
  type        = string
}

variable "identity_id" {
  description = "User-assigned managed identity (registry pulls, Key Vault, storage, database, telemetry)."
  type        = string
}

variable "registry_server" {
  description = "Container registry login server."
  type        = string
}

variable "image" {
  description = "Initial image. Later deployments update the image via CI/CD (changes are ignored here)."
  type        = string
}

variable "cpu" {
  description = "vCPU per replica (valid Consumption combinations: 0.25/0.5Gi ... 4/8Gi)."
  type        = number
}

variable "memory" {
  description = "Memory per replica, e.g. 1Gi."
  type        = string
}

variable "min_replicas" {
  description = "Minimum replicas."
  type        = number
  default     = 1
}

variable "max_replicas" {
  description = "Maximum replicas."
  type        = number
  default     = 1
}

variable "env" {
  description = "Environment variables (non-secret; secrets are read from Key Vault by the app itself)."
  type        = map(string)
  default     = {}
}

variable "ingress" {
  description = "External HTTP ingress, or null for no ingress (worker)."
  type = object({
    target_port         = number
    concurrent_requests = optional(number, 50)
  })
  default = null
}

variable "port" {
  description = "Container port serving /healthz and /readyz."
  type        = number
  default     = 8080
}

variable "termination_grace_period_seconds" {
  description = "Time for in-flight work to finish on shutdown."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Tags."
  type        = map(string)
  default     = {}
}
