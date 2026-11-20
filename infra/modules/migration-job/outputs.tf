output "name" {
  description = "Job name (az containerapp job start --name ...)."
  value       = azurerm_container_app_job.this.name
}
