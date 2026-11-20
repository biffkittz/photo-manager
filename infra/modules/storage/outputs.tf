output "id" {
  description = "Storage account id."
  value       = azurerm_storage_account.this.id
}

output "name" {
  description = "Storage account name."
  value       = azurerm_storage_account.this.name
}

output "blob_endpoint" {
  description = "Blob service endpoint (Blob:ServiceUri)."
  value       = azurerm_storage_account.this.primary_blob_endpoint
}
