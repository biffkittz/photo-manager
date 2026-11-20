# Module: storage

Storage account for staging uploads, renditions, prepared downloads and the Data Protection key ring: public network access and shared keys disabled, containers managed through Azure Resource Manager, lifecycle deletion of staging and downloads after one day, a blob private endpoint and container-scoped role assignments.

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azurerm | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_private_endpoint.blob](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_endpoint) | resource |
| [azurerm_role_assignment.containers](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_storage_account.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_account) | resource |
| [azurerm_storage_container.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_container) | resource |
| [azurerm_storage_management_policy.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_management_policy) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| location | Azure region. | `string` | n/a | yes |
| name | Storage account name (3-24 lowercase letters/digits, globally unique). | `string` | n/a | yes |
| private\_dns\_zone\_id | privatelink.blob.core.windows.net zone id. | `string` | n/a | yes |
| private\_endpoint\_subnet\_id | Subnet for the blob private endpoint. | `string` | n/a | yes |
| resource\_group\_name | Resource group. | `string` | n/a | yes |
| container\_role\_assignments | Container-scoped data-plane roles, keyed by a static name. | ```map(object({ container = string principal_id = string role = string }))``` | `{}` | no |
| containers | Private blob containers to create. | `list(string)` | ```[ "staging", "renditions", "downloads", "dataprotection" ]``` | no |
| expiring\_containers | Containers whose blobs are deleted one day after their last modification. | `list(string)` | ```[ "staging", "downloads" ]``` | no |
| replication\_type | Replication (renditions can be regenerated from the archive; staging/downloads are transient). | `string` | `"LRS"` | no |
| tags | Tags. | `map(string)` | `{}` | no |

## Outputs

| Name | Description |
|------|-------------|
| blob\_endpoint | Blob service endpoint (Blob:ServiceUri). |
| id | Storage account id. |
| name | Storage account name. |
<!-- END_TF_DOCS -->
