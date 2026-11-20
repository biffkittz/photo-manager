# Module: custom-domain

Custom hostname for a Container App: Cloudflare CNAME and asuid TXT records, the Container Apps hostname, a managed certificate (CNAME validation), and the SNI binding (azapi_update_resource, because azurerm cannot bind a managed certificate yet).

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azapi | n/a |
| azurerm | n/a |
| cloudflare | n/a |

## Resources

| Name | Type |
|------|------|
| [azapi_update_resource.bind_certificate](https://registry.terraform.io/providers/azure/azapi/latest/docs/resources/update_resource) | resource |
| [azurerm_container_app_custom_domain.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app_custom_domain) | resource |
| [azurerm_container_app_environment_managed_certificate.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app_environment_managed_certificate) | resource |
| [cloudflare_dns_record.cname](https://registry.terraform.io/providers/cloudflare/cloudflare/latest/docs/resources/dns_record) | resource |
| [cloudflare_dns_record.verification](https://registry.terraform.io/providers/cloudflare/cloudflare/latest/docs/resources/dns_record) | resource |
| [cloudflare_zone.this](https://registry.terraform.io/providers/cloudflare/cloudflare/latest/docs/data-sources/zone) | data source |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| cloudflare\_zone\_name | Cloudflare zone that contains the hostname, e.g. biffkittz.com. | `string` | n/a | yes |
| container\_app\_fqdn | Web Container App ingress FQDN (CNAME target). | `string` | n/a | yes |
| container\_app\_id | Web Container App id. | `string` | n/a | yes |
| environment\_id | Container Apps environment id (owns the managed certificate). | `string` | n/a | yes |
| hostname | Custom hostname, e.g. photo.biffkittz.com. | `string` | n/a | yes |
| verification\_id | Container App custom domain verification id (asuid TXT value). | `string` | n/a | yes |

## Outputs

| Name | Description |
|------|-------------|
| url | Public URL. |
<!-- END_TF_DOCS -->
