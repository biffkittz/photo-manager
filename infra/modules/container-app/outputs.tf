output "id" {
  description = "Container App id."
  value       = azurerm_container_app.this.id
}

output "name" {
  description = "Container App name."
  value       = azurerm_container_app.this.name
}

output "fqdn" {
  description = "Stable ingress FQDN (null without ingress)."
  value       = var.ingress == null ? null : azurerm_container_app.this.ingress[0].fqdn
}

output "custom_domain_verification_id" {
  description = "Value for the asuid TXT record that proves domain ownership."
  value       = azurerm_container_app.this.custom_domain_verification_id
  sensitive   = true
}
