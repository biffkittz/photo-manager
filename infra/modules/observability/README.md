# Module: observability

Log Analytics workspace and workspace-based Application Insights with local (key-based) authentication disabled: telemetry is ingested with Entra ID (the apps' managed identities hold Monitoring Metrics Publisher).

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azurerm | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_application_insights.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/application_insights) | resource |
| [azurerm_log_analytics_workspace.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/log_analytics_workspace) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| location | Azure region. | `string` | n/a | yes |
| name | Base name, e.g. photo-prod. | `string` | n/a | yes |
| resource\_group\_name | Resource group. | `string` | n/a | yes |
| retention\_in\_days | Log retention. | `number` | `30` | no |
| tags | Tags. | `map(string)` | `{}` | no |

## Outputs

| Name | Description |
|------|-------------|
| application\_insights\_connection\_string | Application Insights connection string (not a credential: local auth is disabled). |
| application\_insights\_id | Application Insights id (scope for Monitoring Metrics Publisher). |
| log\_analytics\_workspace\_id | Log Analytics workspace id. |
<!-- END_TF_DOCS -->
