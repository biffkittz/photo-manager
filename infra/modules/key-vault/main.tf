# One vault per tier (web / worker) so each managed identity can read exactly one vault: the web identity
# never gets a path to the archive password (split keys, docs/adr/0001-split-keys.md). Terraform manages
# the vault but never its secrets: values are set out of band (scripts/bootstrap-keyvault.sh) so they never
# enter Terraform state or CI logs.
resource "azurerm_key_vault" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  tenant_id                     = var.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = length(var.admin_ip_ranges) > 0
  tags                          = var.tags

  network_acls {
    bypass         = "None"
    default_action = "Deny"
    ip_rules       = var.admin_ip_ranges
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_private_endpoint" "this" {
  name                = "pe-${var.name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  subnet_id           = var.private_endpoint_subnet_id
  tags                = var.tags

  private_service_connection {
    name                           = "psc-${var.name}"
    private_connection_resource_id = azurerm_key_vault.this.id
    subresource_names              = ["vault"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [var.private_dns_zone_id]
  }
}

resource "azurerm_role_assignment" "secret_readers" {
  for_each             = var.secret_reader_principal_ids
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "crypto_users" {
  for_each             = var.crypto_user_principal_ids
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Crypto User"
  principal_id         = each.value
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "secrets_officers" {
  for_each             = toset(var.admin_object_ids)
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "crypto_officers" {
  for_each             = toset(var.admin_object_ids)
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Crypto Officer"
  principal_id         = each.value
}

# Every secret read and key operation is audited to Log Analytics.
resource "azurerm_monitor_diagnostic_setting" "this" {
  name                       = "audit-to-log-analytics"
  target_resource_id         = azurerm_key_vault.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category = "AuditEvent"
  }
}
