# Module: postgres

Azure Database for PostgreSQL flexible server, VNet-integrated (no public endpoint), Entra-only authentication with the given Entra administrators, the photomanager database, backups, a maintenance window, connection logging and diagnostics. Protected with prevent_destroy.

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azurerm | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_monitor_diagnostic_setting.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/monitor_diagnostic_setting) | resource |
| [azurerm_postgresql_flexible_server.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server) | resource |
| [azurerm_postgresql_flexible_server_active_directory_administrator.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server_active_directory_administrator) | resource |
| [azurerm_postgresql_flexible_server_configuration.log_connections](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server_configuration) | resource |
| [azurerm_postgresql_flexible_server_database.app](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server_database) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| delegated\_subnet\_id | Delegated subnet (VNet integration). | `string` | n/a | yes |
| entra\_administrators | Microsoft Entra administrators (the migrator identity, plus optional break-glass users/groups), keyed by a static name. | ```map(object({ object_id = string principal_name = string principal_type = string # ServicePrincipal | User | Group }))``` | n/a | yes |
| location | Azure region. | `string` | n/a | yes |
| log\_analytics\_workspace\_id | Workspace receiving server logs. | `string` | n/a | yes |
| name | Server name (globally unique). | `string` | n/a | yes |
| private\_dns\_zone\_id | Private DNS zone for the server. | `string` | n/a | yes |
| resource\_group\_name | Resource group. | `string` | n/a | yes |
| tenant\_id | Microsoft Entra tenant id. | `string` | n/a | yes |
| backup\_retention\_days | Point-in-time restore window (7-35 days). | `number` | `14` | no |
| database\_name | Application database. | `string` | `"photomanager"` | no |
| postgres\_version | PostgreSQL major version. | `string` | `"17"` | no |
| sku\_name | SKU (B\_Standard\_B1ms is the smallest burstable tier). | `string` | `"B_Standard_B1ms"` | no |
| storage\_mb | Storage size in MB. | `number` | `32768` | no |
| tags | Tags. | `map(string)` | `{}` | no |

## Outputs

| Name | Description |
|------|-------------|
| database\_name | Application database name. |
| fqdn | Server FQDN (resolves to a private IP inside the VNet). |
| id | Server id. |
<!-- END_TF_DOCS -->
