output "id" {
  description = "Environment id."
  value       = azurerm_container_app_environment.this.id
}

output "default_domain" {
  description = "Environment default domain."
  value       = azurerm_container_app_environment.this.default_domain
}
