output "vnet_id" {
  description = "VNet id."
  value       = azurerm_virtual_network.this.id
}

output "apps_subnet_id" {
  description = "Container Apps environment subnet."
  value       = azurerm_subnet.apps.id
}

output "postgres_subnet_id" {
  description = "Delegated PostgreSQL subnet."
  value       = azurerm_subnet.postgres.id
}

output "endpoints_subnet_id" {
  description = "Private endpoint subnet."
  value       = azurerm_subnet.endpoints.id
}

output "key_vault_dns_zone_id" {
  description = "privatelink.vaultcore.azure.net zone id."
  value       = azurerm_private_dns_zone.this["privatelink.vaultcore.azure.net"].id
  # Consumers (private endpoints, then the apps) must not come up before the zone resolves inside the VNet.
  depends_on = [azurerm_private_dns_zone_virtual_network_link.this]
}

output "blob_dns_zone_id" {
  description = "privatelink.blob.core.windows.net zone id."
  value       = azurerm_private_dns_zone.this["privatelink.blob.core.windows.net"].id
  depends_on  = [azurerm_private_dns_zone_virtual_network_link.this]
}

output "postgres_dns_zone_id" {
  description = "Private DNS zone for the PostgreSQL server."
  value       = azurerm_private_dns_zone.this["${var.name}.private.postgres.database.azure.com"].id
  # The server can only be created once the zone is linked to the VNet.
  depends_on = [azurerm_private_dns_zone_virtual_network_link.this]
}
