# Workload-profiles environment (Consumption profile) integrated into the VNet, so apps reach PostgreSQL,
# Key Vault and Blob Storage over private addresses. The environment's ingress stays public because the
# web app must be reachable at photo.biffkittz.com; the worker has no ingress at all.
resource "azurerm_container_app_environment" "this" {
  name                           = "cae-${var.name}"
  resource_group_name            = var.resource_group_name
  location                       = var.location
  infrastructure_subnet_id       = var.infrastructure_subnet_id
  internal_load_balancer_enabled = false
  zone_redundancy_enabled        = var.zone_redundancy_enabled
  log_analytics_workspace_id     = var.log_analytics_workspace_id
  logs_destination               = "log-analytics"
  tags                           = var.tags

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }
}
