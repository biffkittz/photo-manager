output "log_analytics_workspace_id" {
  description = "Log Analytics workspace id."
  value       = azurerm_log_analytics_workspace.this.id
}

output "application_insights_id" {
  description = "Application Insights id (scope for Monitoring Metrics Publisher)."
  value       = azurerm_application_insights.this.id
}

output "application_insights_connection_string" {
  description = "Application Insights connection string (not a credential: local auth is disabled)."
  value       = azurerm_application_insights.this.connection_string
  sensitive   = true
}
