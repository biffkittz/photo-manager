# Module: network

Virtual network for the deployment: a delegated Container Apps subnet, a delegated PostgreSQL subnet and a private-endpoint subnet, each behind an NSG that only admits traffic from the apps subnet, plus the private DNS zones (Key Vault, Blob, PostgreSQL) linked to the VNet. See docs/adr/0003-private-networking.md.

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azurerm | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_network_security_group.endpoints](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/network_security_group) | resource |
| [azurerm_network_security_group.postgres](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/network_security_group) | resource |
| [azurerm_private_dns_zone.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_dns_zone) | resource |
| [azurerm_private_dns_zone_virtual_network_link.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_dns_zone_virtual_network_link) | resource |
| [azurerm_subnet.apps](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet) | resource |
| [azurerm_subnet.endpoints](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet) | resource |
| [azurerm_subnet.postgres](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet) | resource |
| [azurerm_subnet_network_security_group_association.endpoints](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet_network_security_group_association) | resource |
| [azurerm_subnet_network_security_group_association.postgres](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet_network_security_group_association) | resource |
| [azurerm_virtual_network.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/virtual_network) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| location | Azure region. | `string` | n/a | yes |
| name | Base name, e.g. photo-prod. | `string` | n/a | yes |
| resource\_group\_name | Resource group. | `string` | n/a | yes |
| address\_space | VNet address space (a /16 is carved into the subnets below). | `string` | `"10.40.0.0/16"` | no |
| tags | Tags. | `map(string)` | `{}` | no |

## Outputs

| Name | Description |
|------|-------------|
| apps\_subnet\_id | Container Apps environment subnet. |
| blob\_dns\_zone\_id | privatelink.blob.core.windows.net zone id. |
| endpoints\_subnet\_id | Private endpoint subnet. |
| key\_vault\_dns\_zone\_id | privatelink.vaultcore.azure.net zone id. |
| postgres\_dns\_zone\_id | Private DNS zone for the PostgreSQL server. |
| postgres\_subnet\_id | Delegated PostgreSQL subnet. |
| vnet\_id | VNet id. |
<!-- END_TF_DOCS -->
