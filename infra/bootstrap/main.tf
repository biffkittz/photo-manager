locals {
  suffix = var.name_suffix != "" ? var.name_suffix : substr(sha1("${var.subscription_id}-${var.project}"), 0, 6)
  tags = merge(var.tags, {
    project     = var.project
    environment = var.environment
    managed-by  = "terraform"
    layer       = "bootstrap"
  })

  github_oidc_issuer = "https://token.actions.githubusercontent.com"

  # The Terraform state lives in an existing storage account shared with other projects (see versions.tf).
  # Its id is built from names rather than read with a data source, which would copy that account's access
  # keys into this configuration's state.
  state_container_id = "/subscriptions/${var.subscription_id}/resourceGroups/${var.state_resource_group_name}/providers/Microsoft.Storage/storageAccounts/${var.state_storage_account_name}/blobServices/default/containers/${var.state_container_name}"

  # ABAC condition limiting state access to this project's blobs (photo.*) inside the shared container:
  # reads, writes, leases and deletes need a matching blob path; listings need a matching prefix.
  state_blob_condition = <<-EOT
    (
     (
      !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'} AND NOT SubOperationMatches{'Blob.List'})
      AND
      !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write'})
      AND
      !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action'})
      AND
      !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete'})
     )
     OR
     (
      @Resource[Microsoft.Storage/storageAccounts/blobServices/containers/blobs:path] StringStartsWith '${var.state_blob_prefix}'
     )
    )
    AND
    (
     (
      !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'} AND SubOperationMatches{'Blob.List'})
     )
     OR
     (
      @Request[Microsoft.Storage/storageAccounts/blobServices/containers/blobs:prefix] StringStartsWith '${var.state_blob_prefix}'
     )
    )
  EOT

  # Data-plane roles the deploy identity may grant to the app's managed identities, and nothing else
  # (constrained delegation: it cannot make anyone Owner or grant itself more rights).
  assignable_roles = [
    "AcrPull",
    "Key Vault Secrets User",
    "Key Vault Crypto User",
    "Key Vault Secrets Officer",
    "Key Vault Crypto Officer",
    "Storage Blob Data Contributor",
    "Storage Blob Data Reader",
    "Monitoring Metrics Publisher",
  ]
  assignable_role_guids = join(", ", [for name in local.assignable_roles : basename(data.azurerm_role_definition.assignable[name].id)])
}

data "azurerm_role_definition" "assignable" {
  for_each = toset(local.assignable_roles)
  name     = each.value
}

# ---------------------------------------------------------------------------------------------------
# Application resource group and container registry. The registry lives here (not in the app
# configuration) so CI can push the first images before the Container Apps that use them exist.
# ---------------------------------------------------------------------------------------------------
resource "azurerm_resource_group" "app" {
  name     = "rg-${var.project}-${var.environment}"
  location = var.location
  tags     = merge(local.tags, { layer = "app" })
}

resource "azurerm_container_registry" "this" {
  name                   = "cr${var.project}${local.suffix}"
  resource_group_name    = azurerm_resource_group.app.name
  location               = azurerm_resource_group.app.location
  sku                    = "Basic"
  admin_enabled          = false # pulls use managed identity (AcrPull); no shared credentials exist
  anonymous_pull_enabled = false
  tags                   = local.tags
}

# ---------------------------------------------------------------------------------------------------
# GitHub Actions identities (OIDC federation: no client secrets stored anywhere).
#   deploy - used only by jobs in the protected "production" environment (apply, image deploy)
#   plan   - used by pull-request plans; read-only plus the few "list" actions terraform plan needs
# They live in their own resource group, outside the app resource group the deploy identity manages, so it
# cannot edit its own federated credentials (for example to trust another repository).
# ---------------------------------------------------------------------------------------------------
resource "azurerm_resource_group" "cicd" {
  name     = "rg-${var.project}-cicd"
  location = var.location
  tags     = local.tags
}

resource "azurerm_user_assigned_identity" "github_deploy" {
  name                = "id-${var.project}-github-deploy"
  resource_group_name = azurerm_resource_group.cicd.name
  location            = azurerm_resource_group.cicd.location
  tags                = local.tags
}

resource "azurerm_federated_identity_credential" "github_deploy" {
  name                      = "github-environment-production"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_deploy.id
  issuer                    = local.github_oidc_issuer
  audience                  = ["api://AzureADTokenExchange"]
  subject                   = "repo:${var.github_repository}:environment:production"
}

resource "azurerm_user_assigned_identity" "github_plan" {
  name                = "id-${var.project}-github-plan"
  resource_group_name = azurerm_resource_group.cicd.name
  location            = azurerm_resource_group.cicd.location
  tags                = local.tags
}

resource "azurerm_federated_identity_credential" "github_plan" {
  name                      = "github-pull-request"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_plan.id
  issuer                    = local.github_oidc_issuer
  audience                  = ["api://AzureADTokenExchange"]
  subject                   = "repo:${var.github_repository}:pull_request"
}

# Deploy identity: manage the app resource group, push images, use the state, and assign only the
# whitelisted data-plane roles (Role Based Access Control Administrator with an ABAC condition), never to
# itself. Note that this identity is inside the archive's trust boundary regardless: it deploys the worker
# image. The protected "production" environment (required reviewers) is the control that matters most.
resource "azurerm_role_assignment" "deploy_contributor" {
  scope                = azurerm_resource_group.app.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "deploy_rbac_admin" {
  scope                = azurerm_resource_group.app.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
  principal_type       = "ServicePrincipal"
  condition_version    = "2.0"
  condition            = <<-EOT
    (
     (
      !(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})
     )
     OR
     (
      @Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${local.assignable_role_guids}}
      AND
      @Request[Microsoft.Authorization/roleAssignments:PrincipalId] ForAnyOfAllValues:GuidNotEquals {${azurerm_user_assigned_identity.github_deploy.principal_id}}
     )
    )
    AND
    (
     (
      !(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})
     )
     OR
     (
      @Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${local.assignable_role_guids}}
     )
    )
  EOT
}

resource "azurerm_role_assignment" "deploy_acr_push" {
  scope                = azurerm_container_registry.this.id
  role_definition_name = "AcrPush"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
  principal_type       = "ServicePrincipal"
}

# State: read and write this project's state blobs (photo.*) in the shared container, nothing else there.
resource "azurerm_role_assignment" "deploy_state" {
  scope                = local.state_container_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
  principal_type       = "ServicePrincipal"
  condition_version    = var.scope_state_access_to_prefix ? "2.0" : null
  condition            = var.scope_state_access_to_prefix ? local.state_blob_condition : null
}

# Plan identity: Reader plus the Container Apps "listSecrets" actions, which the azurerm provider calls while
# refreshing (the apps hold no Container Apps secrets). Storage account keys and Log Analytics shared keys are
# deliberately not readable: the provider tolerates being refused those, and they would allow writing data.
resource "azurerm_role_definition" "terraform_planner" {
  name        = "${var.project}-${var.environment}-terraform-planner"
  scope       = azurerm_resource_group.app.id
  description = "Read-only access for terraform plan on Photo Manager, plus the Container Apps list actions the azurerm provider uses during refresh."

  permissions {
    actions = [
      "*/read",
      "Microsoft.App/containerApps/listSecrets/action",
      "Microsoft.App/jobs/listSecrets/action",
    ]
  }

  assignable_scopes = [azurerm_resource_group.app.id]
}

resource "azurerm_role_assignment" "plan_reader" {
  scope              = azurerm_resource_group.app.id
  role_definition_id = azurerm_role_definition.terraform_planner.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.github_plan.principal_id
  principal_type     = "ServicePrincipal"
}

# Pull-request plans run with -lock=false (.github/workflows/terraform.yml), so they only need to read the state:
# code under review can never overwrite or delete it.
resource "azurerm_role_assignment" "plan_state" {
  scope                = local.state_container_id
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_user_assigned_identity.github_plan.principal_id
  principal_type       = "ServicePrincipal"
  condition_version    = var.scope_state_access_to_prefix ? "2.0" : null
  condition            = var.scope_state_access_to_prefix ? local.state_blob_condition : null
}
