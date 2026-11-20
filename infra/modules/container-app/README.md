# Module: container-app

One Container App running as a user-assigned identity: pulls from ACR with that identity, optional external ingress to port 8080 with HTTP scaling, startup/liveness (/healthz) and readiness (/readyz) probes. Image changes are ignored so releases (GitHub Actions) and infrastructure applies never fight. No Container Apps secrets: the app reads Key Vault itself.

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| azurerm | n/a |

## Resources

| Name | Type |
|------|------|
| [azurerm_container_app.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| cpu | vCPU per replica (valid Consumption combinations: 0.25/0.5Gi ... 4/8Gi). | `number` | n/a | yes |
| environment\_id | Container Apps environment id. | `string` | n/a | yes |
| identity\_id | User-assigned managed identity (registry pulls, Key Vault, storage, database, telemetry). | `string` | n/a | yes |
| image | Initial image. Later deployments update the image via CI/CD (changes are ignored here). | `string` | n/a | yes |
| memory | Memory per replica, e.g. 1Gi. | `string` | n/a | yes |
| name | Container App name. | `string` | n/a | yes |
| registry\_server | Container registry login server. | `string` | n/a | yes |
| resource\_group\_name | Resource group. | `string` | n/a | yes |
| env | Environment variables (non-secret; secrets are read from Key Vault by the app itself). | `map(string)` | `{}` | no |
| ingress | External HTTP ingress, or null for no ingress (worker). | ```object({ target_port = number concurrent_requests = optional(number, 50) })``` | `null` | no |
| max\_replicas | Maximum replicas. | `number` | `1` | no |
| min\_replicas | Minimum replicas. | `number` | `1` | no |
| port | Container port serving /healthz and /readyz. | `number` | `8080` | no |
| tags | Tags. | `map(string)` | `{}` | no |
| termination\_grace\_period\_seconds | Time for in-flight work to finish on shutdown. | `number` | `30` | no |

## Outputs

| Name | Description |
|------|-------------|
| custom\_domain\_verification\_id | Value for the asuid TXT record that proves domain ownership. |
| fqdn | Stable ingress FQDN (null without ingress). |
| id | Container App id. |
| name | Container App name. |
<!-- END_TF_DOCS -->
