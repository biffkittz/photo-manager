# Environment: prod

Composes the modules into the production deployment (centralus, photo.biffkittz.com, Temporal Cloud
namespace photo.joqqs). State is `photo.terraform.tfstate` in the existing state storage (configured in
`versions.tf`, Entra ID auth; see [infra/README.md](../../README.md#state)). Copy `terraform.tfvars.example` to
start; it contains no secrets.

Secrets are not Terraform inputs, except the Temporal Cloud API key the provider authenticates with
(`TF_VAR_temporal_api_key` or `TEMPORAL_CLOUD_API_KEY`) and the Cloudflare token (`CLOUDFLARE_API_TOKEN`).

<!-- BEGIN_TF_DOCS -->
## Requirements

| Name | Version |
|------|---------|
| terraform | >= 1.17.0, < 2.0.0 |
| azapi | ~> 2.14 |
| azurerm | ~> 5.14 |
| cloudflare | ~> 5.29 |
| temporalcloud | ~> 1.13 |

## Providers

| Name | Version |
|------|---------|
| azurerm | ~> 5.14 |

## Modules

| Name | Source | Version |
|------|--------|---------|
| container\_apps\_environment | ../../modules/container-apps-environment | n/a |
| custom\_domain | ../../modules/custom-domain | n/a |
| key\_vault\_web | ../../modules/key-vault | n/a |
| key\_vault\_worker | ../../modules/key-vault | n/a |
| migration\_job | ../../modules/migration-job | n/a |
| network | ../../modules/network | n/a |
| observability | ../../modules/observability | n/a |
| postgres | ../../modules/postgres | n/a |
| storage | ../../modules/storage | n/a |
| temporal\_cloud | ../../modules/temporal-cloud | n/a |
| web\_app | ../../modules/container-app | n/a |
| worker\_app | ../../modules/container-app | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_role_assignment.acr_pull](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.telemetry](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_user_assigned_identity.app](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/user_assigned_identity) | resource |
| [azurerm_client_config.current](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/data-sources/client_config) | data source |
| [azurerm_resource_group.app](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/data-sources/resource_group) | data source |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| github\_users | GitHub accounts allowed to sign in, by immutable numeric id (curl -s https://api.github.com/users/<login> \| jq .id). | ```list(object({ id = number login = string role = string }))``` | n/a | yes |
| image\_tag | Image tag used when the Container Apps are first created (CI/CD deploys later tags). | `string` | n/a | yes |
| subscription\_id | Azure subscription id. | `string` | n/a | yes |
| admin\_ip\_ranges | Operator public IPs/CIDRs allowed to reach the Key Vaults (to set secrets). Empty = private endpoints only. | `list(string)` | `[]` | no |
| cloudflare\_zone\_name | Cloudflare zone containing the hostname. | `string` | `"biffkittz.com"` | no |
| container\_registry\_login\_server | Login server of the registry; empty means <name>.azurecr.io. Set it only if the registry uses a domain name label (login server with a hash suffix). | `string` | `""` | no |
| container\_registry\_name | Existing container registry holding the images (shared with another project). | `string` | `"crloginappdemo7fyguj"` | no |
| container\_registry\_resource\_group | Resource group of the existing container registry (same subscription). | `string` | `"rg-loginapp-demo"` | no |
| custom\_domain | Public hostname of the site. | `string` | `"photo.biffkittz.com"` | no |
| environment | Environment name (must match infra/bootstrap). | `string` | `"prod"` | no |
| gemini\_model | Gemini model id (pinned). | `string` | `"gemini-3.8-flash"` | no |
| gemini\_requests\_per\_second | Gemini calls per second across all worker replicas (Temporal task-queue rate limit). | `number` | `1` | no |
| key\_vault\_admin\_object\_ids | Entra object ids allowed to manage secrets/keys (Key Vault Secrets Officer + Crypto Officer). | `list(string)` | `[]` | no |
| location | Azure region. | `string` | `"centralus"` | no |
| log\_retention\_days | Log Analytics / Application Insights retention. | `number` | `30` | no |
| manage\_dns | Create the Cloudflare records and the managed certificate for the custom domain. | `bool` | `true` | no |
| manage\_temporal\_cloud | Manage the namespace and service accounts with the temporalcloud provider. | `bool` | `true` | no |
| name\_suffix | Suffix for globally unique names (Key Vaults, storage account, PostgreSQL server); empty derives a stable one from the subscription id. | `string` | `""` | no |
| postgres\_admin\_principals | Optional break-glass Entra administrators for PostgreSQL (the migrator identity is always one). | ```list(object({ object_id = string principal_name = string principal_type = string }))``` | `[]` | no |
| postgres\_sku | PostgreSQL SKU. | `string` | `"B_Standard_B1ms"` | no |
| postgres\_version | PostgreSQL major version. | `string` | `"17"` | no |
| project | Short project name (must match infra/bootstrap). | `string` | `"photo"` | no |
| resource\_group\_name | App resource group created by infra/bootstrap; empty = rg-<project>-<environment>. | `string` | `""` | no |
| tags | Extra tags. | `map(string)` | `{}` | no |
| temporal\_account\_id | Temporal Cloud account id. | `string` | `"joqqs"` | no |
| temporal\_api\_key | Temporal Cloud API key for Terraform (account-level). Prefer TF\_VAR\_temporal\_api\_key or TEMPORAL\_CLOUD\_API\_KEY. | `string` | `null` | no |
| temporal\_grpc\_endpoint | Temporal Cloud gRPC endpoint used by the apps. | `string` | `"photo.joqqs.tmprl.cloud:7233"` | no |
| temporal\_import\_existing\_namespace | Import the existing namespace into state instead of creating it. | `bool` | `true` | no |
| temporal\_namespace\_name | Namespace name without account suffix. | `string` | `"photo"` | no |
| temporal\_namespace\_region | Namespace region with cloud prefix (e.g. aws-us-east-1). Must match the existing namespace. | `string` | `""` | no |
| temporal\_retention\_days | Workflow history retention in days. | `number` | `30` | no |
| vnet\_address\_space | VNet address space (/16). | `string` | `"10.40.0.0/16"` | no |
| wasabi\_bucket | Archive bucket. | `string` | `"photo-bk"` | no |
| wasabi\_region | Wasabi signing region. | `string` | `"us-east-2"` | no |
| wasabi\_service\_url | Wasabi S3 endpoint of the archive bucket. | `string` | `"https://s3.us-east-2.wasabisys.com"` | no |
| web\_max\_replicas | Maximum web replicas. | `number` | `3` | no |
| web\_min\_replicas | Minimum web replicas. Use 0 for the very first apply (before Key Vault secrets and the database exist; see docs/deployment.md), then 1. 0 also allows scale to zero at the cost of cold starts. | `number` | `1` | no |
| worker\_replicas | Worker replicas. Use 0 for the very first apply (before Key Vault secrets and the database exist; see docs/deployment.md), then 1. Temporal recommends two for always-on queues; one keeps cost minimal. | `number` | `1` | no |

## Outputs

| Name | Description |
|------|-------------|
| container\_apps | Container App and job names (used by the deploy workflow). |
| key\_vaults | Key Vault names to populate with scripts/bootstrap-keyvault.sh. |
| managed\_identities | Client ids of the app identities. |
| postgres\_fqdn | PostgreSQL server FQDN (private). |
| site\_url | Public URL of the site. |
| storage\_account | Blob storage account name. |
| temporal\_service\_account\_ids | Create one API key for each of these service accounts in Temporal Cloud. |
| web\_app\_fqdn | Default FQDN of the web Container App. |
<!-- END_TF_DOCS -->
