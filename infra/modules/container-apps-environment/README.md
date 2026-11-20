# Module: container-apps-environment

Container Apps environment (workload profiles, Consumption) in the apps subnet, sending logs to Log Analytics.

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azurerm | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_container_app_environment.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app_environment) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| infrastructure\_subnet\_id | Delegated subnet (/23) for the environment. | `string` | n/a | yes |
| location | Azure region. | `string` | n/a | yes |
| log\_analytics\_workspace\_id | Workspace receiving container logs. | `string` | n/a | yes |
| name | Base name, e.g. photo-prod. | `string` | n/a | yes |
| resource\_group\_name | Resource group. | `string` | n/a | yes |
| tags | Tags. | `map(string)` | `{}` | no |
| zone\_redundancy\_enabled | Spread replicas across availability zones. | `bool` | `false` | no |

## Outputs

| Name | Description |
|------|-------------|
| default\_domain | Environment default domain. |
| id | Environment id. |
<!-- END_TF_DOCS -->
