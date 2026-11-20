variable "hostname" {
  description = "Custom hostname, e.g. photo.biffkittz.com."
  type        = string
}

variable "cloudflare_zone_name" {
  description = "Cloudflare zone that contains the hostname, e.g. biffkittz.com."
  type        = string
}

variable "container_app_id" {
  description = "Web Container App id."
  type        = string
}

variable "container_app_fqdn" {
  description = "Web Container App ingress FQDN (CNAME target)."
  type        = string
}

variable "verification_id" {
  description = "Container App custom domain verification id (asuid TXT value)."
  type        = string
  sensitive   = true
}

variable "environment_id" {
  description = "Container Apps environment id (owns the managed certificate)."
  type        = string
}
