output "site_url" {
  description = "Public URL of the site."
  value       = var.manage_dns ? module.custom_domain[0].url : "https://${module.web_app.fqdn}"
}

output "web_app_fqdn" {
  description = "Default FQDN of the web Container App."
  value       = module.web_app.fqdn
}

output "container_apps" {
  description = "Container App and job names (used by the deploy workflow)."
  value = {
    web           = module.web_app.name
    worker        = module.worker_app.name
    migration_job = module.migration_job.name
  }
}

output "key_vaults" {
  description = "Key Vault names to populate with scripts/bootstrap-keyvault.sh."
  value = {
    web    = module.key_vault_web.name
    worker = module.key_vault_worker.name
  }
}

output "managed_identities" {
  description = "Client ids of the app identities."
  value       = { for tier, identity in data.azurerm_user_assigned_identity.app : tier => identity.client_id }
}

output "postgres_fqdn" {
  description = "PostgreSQL server FQDN (private)."
  value       = module.postgres.fqdn
}

output "storage_account" {
  description = "Blob storage account name."
  value       = module.storage.name
}

output "temporal_service_account_ids" {
  description = "Create one API key for each of these service accounts in Temporal Cloud."
  value       = var.manage_temporal_cloud ? module.temporal_cloud[0].service_account_ids : {}
}
