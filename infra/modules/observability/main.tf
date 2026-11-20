resource "azurerm_log_analytics_workspace" "this" {
  name                = "log-${var.name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = "PerGB2018"
  retention_in_days   = var.retention_in_days
  tags                = var.tags
}

# Local (key-based) authentication is disabled: telemetry can only be ingested with a Microsoft Entra token
# of an identity holding "Monitoring Metrics Publisher". The connection string alone is therefore not a
# credential, which is why it can be passed to the apps as a plain environment variable.
resource "azurerm_application_insights" "this" {
  name                         = "appi-${var.name}"
  resource_group_name          = var.resource_group_name
  location                     = var.location
  workspace_id                 = azurerm_log_analytics_workspace.this.id
  application_type             = "web"
  local_authentication_enabled = false
  retention_in_days            = var.retention_in_days
  tags                         = var.tags
}
