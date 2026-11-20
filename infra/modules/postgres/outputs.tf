output "fqdn" {
  description = "Server FQDN (resolves to a private IP inside the VNet)."
  value       = azurerm_postgresql_flexible_server.this.fqdn
}

output "database_name" {
  description = "Application database name."
  value       = azurerm_postgresql_flexible_server_database.app.name
}

output "id" {
  description = "Server id."
  value       = azurerm_postgresql_flexible_server.this.id
}
