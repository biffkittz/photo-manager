output "app_resource_group_name" {
  description = "Resource group for the application (input to environments/prod)."
  value       = azurerm_resource_group.app.name
}

output "container_registry_name" {
  description = "The shared container registry (input to environments/prod)."
  value       = var.container_registry_name
}

output "container_registry_login_server" {
  description = "Registry login server for docker push (GitHub variable ACR_LOGIN_SERVER)."
  value       = local.registry_login_server
}

output "state_backend" {
  description = "The state location the CI identities were granted access to (configured in each versions.tf)."
  value = {
    resource_group_name  = var.state_resource_group_name
    storage_account_name = var.state_storage_account_name
    container_name       = var.state_container_name
    blob_prefix          = var.state_blob_prefix
  }
}

output "github_actions" {
  description = "Values for GitHub repository variables (not secrets)."
  value = {
    AZURE_TENANT_ID        = azurerm_user_assigned_identity.github_deploy.tenant_id
    AZURE_SUBSCRIPTION_ID  = var.subscription_id
    AZURE_CLIENT_ID_DEPLOY = azurerm_user_assigned_identity.github_deploy.client_id
    AZURE_CLIENT_ID_PLAN   = azurerm_user_assigned_identity.github_plan.client_id
    ACR_NAME               = var.container_registry_name
    ACR_LOGIN_SERVER       = local.registry_login_server
  }
}

output "registry_cleanup_task" {
  description = "Name of the weekly image clean-up task in the shared registry (null when disabled)."
  value       = var.registry_cleanup_enabled ? azurerm_container_registry_task.image_cleanup[0].name : null
}
