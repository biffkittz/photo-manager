# Module: migration-job

Manually triggered Container Apps Job that runs the migrator image (EF Core migrations and least-privilege database grants) inside the VNet.

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azurerm | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_container_app_job.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app_job) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| environment\_id | Container Apps environment id. | `string` | n/a | yes |
| identity\_id | Migrator managed identity (PostgreSQL Entra administrator). | `string` | n/a | yes |
| image | Initial image (CI/CD updates it before each run). | `string` | n/a | yes |
| location | Azure region. | `string` | n/a | yes |
| name | Job name. | `string` | n/a | yes |
| registry\_server | Container registry login server. | `string` | n/a | yes |
| resource\_group\_name | Resource group. | `string` | n/a | yes |
| env | Environment variables. | `map(string)` | `{}` | no |
| tags | Tags. | `map(string)` | `{}` | no |

## Outputs

| Name | Description |
|------|-------------|
| name | Job name (az containerapp job start --name ...). |
<!-- END_TF_DOCS -->
