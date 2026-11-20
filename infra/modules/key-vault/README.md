# Module: key-vault

RBAC-mode Key Vault with purge protection, public access disabled (optionally opened to admin IP ranges for bootstrapping), a private endpoint, AuditEvent diagnostics and role assignments for secret readers, crypto users and administrators. Secret values are never managed here (scripts/bootstrap-keyvault.sh).

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azurerm | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_key_vault.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/key_vault) | resource |
| [azurerm_monitor_diagnostic_setting.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/monitor_diagnostic_setting) | resource |
| [azurerm_private_endpoint.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_endpoint) | resource |
| [azurerm_role_assignment.crypto_officers](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.crypto_users](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.secret_readers](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.secrets_officers](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| location | Azure region. | `string` | n/a | yes |
| log\_analytics\_workspace\_id | Workspace receiving the vault's audit log. | `string` | n/a | yes |
| name | Key Vault name (3-24 chars, globally unique). | `string` | n/a | yes |
| private\_dns\_zone\_id | privatelink.vaultcore.azure.net zone id. | `string` | n/a | yes |
| private\_endpoint\_subnet\_id | Subnet for the private endpoint. | `string` | n/a | yes |
| resource\_group\_name | Resource group. | `string` | n/a | yes |
| tenant\_id | Microsoft Entra tenant id. | `string` | n/a | yes |
| admin\_ip\_ranges | Public IPs/CIDRs allowed through the firewall (operators setting secrets). Empty = private endpoint only. | `list(string)` | `[]` | no |
| admin\_object\_ids | Entra object ids that manage secrets and keys (Key Vault Secrets Officer + Crypto Officer). | `list(string)` | `[]` | no |
| crypto\_user\_principal\_ids | Managed identities that use keys for wrap/unwrap (Key Vault Crypto User), keyed by a static name. | `map(string)` | `{}` | no |
| secret\_reader\_principal\_ids | Managed identities that read secrets (Key Vault Secrets User), keyed by a static name. | `map(string)` | `{}` | no |
| tags | Tags. | `map(string)` | `{}` | no |

## Outputs

| Name | Description |
|------|-------------|
| id | Key Vault id. |
| name | Key Vault name. |
| vault\_uri | Key Vault URI (with trailing slash). |
<!-- END_TF_DOCS -->
