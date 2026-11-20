# Bootstrap (run once)

Creates what must exist before the main configuration can run: the application resource group, the container
registry, and the GitHub Actions identities (OIDC federation, no secrets) in their own resource group: a
**deploy** identity for the `production` environment and a read-only **plan** identity for pull requests. Both
get access to this project's `photo.*` blobs in the existing, shared Terraform state container
([infra/README.md](../README.md#state)), where this configuration's own state lives too
(`photo.bootstrap.tfstate`). Run it from a workstation with Owner rights; see
[docs/deployment.md](../../docs/deployment.md#1-bootstrap-once-from-your-workstation).

<!-- BEGIN_TF_DOCS -->
## Requirements

| Name | Version |
|------|---------|
| terraform | >= 1.17.0, < 2.0.0 |
| azurerm | ~> 5.14 |

## Providers

| Name | Version |
|------|---------|
| azurerm | ~> 5.14 |

## Resources

| Name | Type |
|------|------|
| [azurerm_container_registry_task.image_cleanup](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_registry_task) | resource |
| [azurerm_federated_identity_credential.github_deploy](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/federated_identity_credential) | resource |
| [azurerm_federated_identity_credential.github_plan](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/federated_identity_credential) | resource |
| [azurerm_federated_identity_credential.github_plan_main](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/federated_identity_credential) | resource |
| [azurerm_resource_group.app](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/resource_group) | resource |
| [azurerm_resource_group.cicd](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/resource_group) | resource |
| [azurerm_role_assignment.app_acr_pull](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.deploy_acr_push](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.deploy_contributor](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.deploy_rbac_admin](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.deploy_state](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.plan_reader](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.plan_state](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_definition.blob_data_writer](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_definition) | resource |
| [azurerm_role_definition.state_reader](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_definition) | resource |
| [azurerm_role_definition.state_writer](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_definition) | resource |
| [azurerm_role_definition.terraform_planner](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_definition) | resource |
| [azurerm_user_assigned_identity.app](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/user_assigned_identity) | resource |
| [azurerm_user_assigned_identity.github_deploy](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/user_assigned_identity) | resource |
| [azurerm_user_assigned_identity.github_plan](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/user_assigned_identity) | resource |
| [azurerm_role_definition.assignable](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/data-sources/role_definition) | data source |

## Inputs

| Name | Description | Type | Default | Required |
|------|-------------|------|---------|:--------:|
| subscription\_id | Azure subscription that hosts Photo Manager. | `string` | n/a | yes |
| container\_registry\_login\_server | Login server of the registry; empty means <name>.azurecr.io. Set it only if the registry uses a domain name label (login server with a hash suffix). | `string` | `""` | no |
| container\_registry\_name | Existing container registry the images are pushed to (shared with another project). | `string` | `"crloginappdemo7fyguj"` | no |
| container\_registry\_resource\_group | Resource group of the existing container registry (same subscription). | `string` | `"rg-loginapp-demo"` | no |
| environment | Environment name (part of resource names). | `string` | `"prod"` | no |
| github\_repository | GitHub repository (owner/name) whose Actions may sign in to Azure with OIDC. | `string` | `"biffkittz/photo-manager"` | no |
| github\_repository\_ids | Numeric ids of the repository's owner and of the repository, for GitHub's immutable OIDC subject format (repo:<owner>@<owner id>/<name>@<repository id>:...), which survives renames and cannot be claimed by a re-registered account or repository. Shown as "subject claim" in the azure/login log, or: gh api repos/<owner>/<name> --jq '.owner.id, .id'. Null only if your tokens use the classic format (repo:<owner>/<name>:...). | ```object({ owner = number repository = number })``` | ```{ "owner": 6528701, "repository": 1464743054 }``` | no |
| location | Azure region for all resources. | `string` | `"centralus"` | no |
| project | Short project name used in resource names. | `string` | `"photo"` | no |
| registry\_cleanup\_dry\_run | Only log what the clean-up would delete (az acr task logs); nothing is removed. | `bool` | `false` | no |
| registry\_cleanup\_enabled | Run the weekly clean-up of this project's images (photo-web, photo-worker, photo-migrator) in the shared registry. | `bool` | `true` | no |
| registry\_cleanup\_keep | Always keep this many of the newest candidate tags per repository (rollback targets). | `number` | `10` | no |
| registry\_cleanup\_older\_than\_days | Image tags older than this many days are candidates for deletion. | `number` | `30` | no |
| scope\_state\_access\_to\_prefix | Limit the CI identities' state access to blobs named state\_blob\_prefix* with an ABAC condition. Set false only if the condition is rejected for the state account (CI could then use every blob in the container). | `bool` | `true` | no |
| state\_blob\_prefix | Blob-name prefix the CI identities may use in the shared state container. The default covers only the production state (photo.terraform.tfstate and its workspace states), not this configuration's own photo.bootstrap.tfstate. | `string` | `"photo.terraform.tfstate"` | no |
| state\_container\_name | Blob container holding the state files (must match the backend blocks). | `string` | `"terraform"` | no |
| state\_resource\_group\_name | Resource group of the existing Terraform state storage account (must match the backend blocks). | `string` | `"rg-terraform-state"` | no |
| state\_storage\_account\_name | Existing Terraform state storage account (must match the backend blocks). | `string` | `"demonstration"` | no |
| tags | Extra tags for every resource. | `map(string)` | `{}` | no |

## Outputs

| Name | Description |
|------|-------------|
| app\_resource\_group\_name | Resource group for the application (input to environments/prod). |
| container\_registry\_login\_server | Registry login server for docker push (GitHub variable ACR\_LOGIN\_SERVER). |
| container\_registry\_name | The shared container registry (input to environments/prod). |
| github\_actions | Values for GitHub repository variables (not secrets). |
| registry\_cleanup\_task | Name of the weekly image clean-up task in the shared registry (null when disabled). |
| state\_backend | The state location the CI identities were granted access to (configured in each versions.tf). |
<!-- END_TF_DOCS -->
