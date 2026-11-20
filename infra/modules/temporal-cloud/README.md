# Module: temporal-cloud

Temporal Cloud namespace settings (API-key auth, retention, delete protection) and one namespace-scoped service account per tier. API keys are created outside Terraform so they never land in state.

<!-- BEGIN_TF_DOCS -->


## Providers

| Name | Version |
|------|---------|
| temporalcloud | n/a |

## Resources

| Name | Type |
|------|------|
| [temporalcloud_namespace.this](https://registry.terraform.io/providers/temporalio/temporalcloud/latest/docs/resources/namespace) | resource |
| [temporalcloud_service_account.web](https://registry.terraform.io/providers/temporalio/temporalcloud/latest/docs/resources/service_account) | resource |
| [temporalcloud_service_account.worker](https://registry.terraform.io/providers/temporalio/temporalcloud/latest/docs/resources/service_account) | resource |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| namespace\_name | Namespace name without the account suffix (photo for photo.joqqs). | `string` | n/a | yes |
| region | Namespace region with cloud prefix, e.g. aws-us-east-1 (must match an existing namespace; regions cannot change). | `string` | n/a | yes |
| service\_account\_prefix | Prefix for the per-tier service accounts. | `string` | n/a | yes |
| retention\_days | Workflow history retention (1-90 days). | `number` | `30` | no |

## Outputs

| Name | Description |
|------|-------------|
| grpc\_endpoint | gRPC endpoint for API-key clients. |
| namespace\_id | Namespace id, e.g. photo.joqqs. |
| service\_account\_ids | Service accounts that need API keys (create in the Temporal Cloud UI). |
<!-- END_TF_DOCS -->
