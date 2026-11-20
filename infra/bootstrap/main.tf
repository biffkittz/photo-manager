locals {
  tags = merge(var.tags, {
    project     = var.project
    environment = var.environment
    managed-by  = "terraform"
    layer       = "bootstrap"
  })

  github_oidc_issuer = "https://token.actions.githubusercontent.com"

  # Subject prefix of this repository's GitHub OIDC tokens. GitHub issues the immutable form, which pins the
  # numeric owner and repository ids (repo:<owner>@<id>/<name>@<id>), so a renamed, deleted or re-registered
  # account or repository can never inherit this trust; the classic form is kept for var.github_repository_ids = null.
  github_subject_prefix = (
    var.github_repository_ids == null
    ? "repo:${var.github_repository}"
    : format(
      "repo:%s@%d/%s@%d",
      split("/", var.github_repository)[0],
      var.github_repository_ids.owner,
      split("/", var.github_repository)[1],
      var.github_repository_ids.repository,
    )
  )

  # The container registry is an existing one shared with another project. Like the state account below, it is
  # referenced by name rather than read with a data source, which would copy its admin password (if the admin
  # user is enabled) into this configuration's state.
  registry_id           = "/subscriptions/${var.subscription_id}/resourceGroups/${var.container_registry_resource_group}/providers/Microsoft.ContainerRegistry/registries/${var.container_registry_name}"
  registry_login_server = var.container_registry_login_server != "" ? var.container_registry_login_server : "${lower(var.container_registry_name)}.azurecr.io"

  # The Terraform state lives in an existing storage account shared with other projects (see versions.tf).
  # Its ids are built from names rather than read with a data source, which would copy that account's access
  # keys into this configuration's state.
  state_resource_group_id = "/subscriptions/${var.subscription_id}/resourceGroups/${var.state_resource_group_name}"
  state_container_id      = "${local.state_resource_group_id}/providers/Microsoft.Storage/storageAccounts/${var.state_storage_account_name}/blobServices/default/containers/${var.state_container_name}"

  # ABAC conditions limiting the CI identities to the production state inside the shared container (blobs whose
  # name starts with var.state_blob_prefix, by default photo.terraform.tfstate and its workspace states):
  # reads, writes, leases and deletes need a matching blob path; listings need a matching prefix. This
  # configuration's own state (photo.bootstrap.tfstate) does not match, so CI can neither read nor change it.
  # Azure rejects a condition that mentions an action its role does not grant, so the read-only role gets a
  # condition that names reads only.
  state_blob_list_condition = <<-EOT
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

  state_write_condition = <<-EOT
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
    ${trimspace(local.state_blob_list_condition)}
  EOT

  state_read_condition = <<-EOT
    (
     (
      !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'} AND NOT SubOperationMatches{'Blob.List'})
     )
     OR
     (
      @Resource[Microsoft.Storage/storageAccounts/blobServices/containers/blobs:path] StringStartsWith '${var.state_blob_prefix}'
     )
    )
    AND
    ${trimspace(local.state_blob_list_condition)}
  EOT

  # Blob permissions for the custom roles below. None of them can write or delete a container: the built-in
  # Storage Blob Data roles can, conditions on an assignment do not limit that, and containers/write is also what
  # authorizes Undelete Blob (restoring soft-deleted blobs). Reading container properties is harmless.
  container_read_actions = ["Microsoft.Storage/storageAccounts/blobServices/containers/read"]
  blob_read_actions      = ["Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read"]
  blob_write_actions = [
    "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read",
    "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write",
    "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action",
    "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete",
  ]

  # Roles the deploy identity may grant to the app's managed identities, and nothing else (constrained
  # delegation: it cannot make anyone Owner or grant itself more rights). Blob write access is only grantable
  # through the custom role below, never the built-in Storage Blob Data Contributor.
  assignable_roles = [
    "Key Vault Secrets User",
    "Key Vault Crypto User",
    "Key Vault Secrets Officer",
    "Key Vault Crypto Officer",
    "Storage Blob Data Reader",
    "Monitoring Metrics Publisher",
  ]
  assignable_role_guids = join(", ", concat(
    [for name in local.assignable_roles : basename(data.azurerm_role_definition.assignable[name].id)],
    [azurerm_role_definition.blob_data_writer.role_definition_id],
  ))
}

data "azurerm_role_definition" "assignable" {
  for_each = toset(local.assignable_roles)
  name     = each.value
}

# ---------------------------------------------------------------------------------------------------
# Application resource group and identities. Images go to the existing, shared container registry
# (var.container_registry_*), so CI can push the first images before the Container Apps exist.
# ---------------------------------------------------------------------------------------------------
resource "azurerm_resource_group" "app" {
  name     = "rg-${var.project}-${var.environment}"
  location = var.location
  tags     = merge(local.tags, { layer = "app" })
}

# One identity per tier, so each gets only the rights its tier needs (the production configuration grants the
# Key Vault, storage and telemetry roles). They are created here, by the operator, together with their pull
# access to the shared registry: CI then needs no right to grant roles on a registry another project uses.
# The database roles are bound to their object ids, so they must never be recreated.
resource "azurerm_user_assigned_identity" "app" {
  for_each            = toset(["web", "worker", "migrator"])
  name                = "id-${var.project}-${var.environment}-${each.key}"
  resource_group_name = azurerm_resource_group.app.name
  location            = azurerm_resource_group.app.location
  tags                = merge(local.tags, { layer = "app" })

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_role_assignment" "app_acr_pull" {
  for_each             = azurerm_user_assigned_identity.app
  scope                = local.registry_id
  role_definition_name = "AcrPull"
  principal_id         = each.value.principal_id
  principal_type       = "ServicePrincipal"
}

# Blob access for the app identities, assigned per container by the production configuration: read, write and
# delete blobs, nothing else. With the built-in Storage Blob Data Contributor, a compromised web tier could
# restore staged uploads that ingest had already deleted (blob soft delete keeps them for 7 days) and delete
# whole containers.
resource "azurerm_role_definition" "blob_data_writer" {
  name        = "${var.project}-${var.environment}-blob-data-writer"
  scope       = azurerm_resource_group.app.id
  description = "Photo Manager apps: read, write and delete blobs. No container management and no undelete."

  permissions {
    actions      = local.container_read_actions
    data_actions = local.blob_write_actions
  }

  assignable_scopes = [azurerm_resource_group.app.id]
}

# ---------------------------------------------------------------------------------------------------
# GitHub Actions identities (OIDC federation: no client secrets stored anywhere).
#   deploy - used only by jobs in the protected "production" environment (apply, image deploy)
#   plan   - used by plans (pull requests, and main before an apply is approved); read-only plus the few
#            "list" actions terraform plan needs
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
  subject                   = "${local.github_subject_prefix}:environment:production"
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
  subject                   = "${local.github_subject_prefix}:pull_request"
}

# Plans of main, shown in the run before anyone approves the apply (.github/workflows/terraform.yml). Azure
# rejects concurrent changes to one identity's federated credentials, hence the explicit ordering.
resource "azurerm_federated_identity_credential" "github_plan_main" {
  name                      = "github-branch-main"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_plan.id
  issuer                    = local.github_oidc_issuer
  audience                  = ["api://AzureADTokenExchange"]
  subject                   = "${local.github_subject_prefix}:ref:refs/heads/main"

  depends_on = [azurerm_federated_identity_credential.github_plan]
}

# Deploy identity: manage the app resource group, push images, use the state, and assign only the
# whitelisted roles (Role Based Access Control Administrator with an ABAC condition), never to itself. Note
# that this identity is inside the archive's trust boundary regardless: it deploys the worker image. The
# protected "production" environment (required reviewers) is the control that matters most.
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

# Registry: push this project's images (photo-web, photo-worker, photo-migrator). AcrPush applies to the whole
# shared registry; repository-level scoping would require switching the registry to ABAC repository
# permissions, which would change how the other project's existing role assignments work. The deploy identity
# has no right to grant or remove roles on the registry: the app identities' AcrPull is assigned above.
resource "azurerm_role_assignment" "deploy_acr_push" {
  scope                = local.registry_id
  role_definition_name = "AcrPush"
  principal_id         = azurerm_user_assigned_identity.github_deploy.principal_id
  principal_type       = "ServicePrincipal"
}

# State: the CI identities get custom roles on the shared container instead of the built-in Storage Blob Data
# roles, so they can never delete or reconfigure the container (and with it other projects' state). The roles
# are defined in the state account's resource group and assigned to the container, with the condition above.
resource "azurerm_role_definition" "state_writer" {
  name        = "${var.project}-${var.environment}-terraform-state-writer"
  scope       = local.state_resource_group_id
  description = "Photo Manager CI: read, write, lease and delete its Terraform state blobs (narrowed by a condition on the assignment)."

  permissions {
    actions      = local.container_read_actions
    data_actions = local.blob_write_actions
  }

  assignable_scopes = [local.state_resource_group_id]
}

resource "azurerm_role_definition" "state_reader" {
  name        = "${var.project}-${var.environment}-terraform-state-reader"
  scope       = local.state_resource_group_id
  description = "Photo Manager plans: read its Terraform state blob (narrowed by a condition on the assignment)."

  permissions {
    actions      = local.container_read_actions
    data_actions = local.blob_read_actions
  }

  assignable_scopes = [local.state_resource_group_id]
}

resource "azurerm_role_assignment" "deploy_state" {
  scope              = local.state_container_id
  role_definition_id = azurerm_role_definition.state_writer.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.github_deploy.principal_id
  principal_type     = "ServicePrincipal"
  condition_version  = var.scope_state_access_to_prefix ? "2.0" : null
  condition          = var.scope_state_access_to_prefix ? local.state_write_condition : null
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

# Plans run with -lock=false (.github/workflows/terraform.yml), so they only need to read the state: code
# under review can never overwrite or delete it.
resource "azurerm_role_assignment" "plan_state" {
  scope              = local.state_container_id
  role_definition_id = azurerm_role_definition.state_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.github_plan.principal_id
  principal_type     = "ServicePrincipal"
  condition_version  = var.scope_state_access_to_prefix ? "2.0" : null
  condition          = var.scope_state_access_to_prefix ? local.state_read_condition : null
}

# ---------------------------------------------------------------------------------------------------
# Weekly image clean-up in the shared registry (an ACR task: it runs inside the registry on a timer, so no
# extra identity or CI secret is involved). It only matches this project's repositories: the repository
# filter must match the whole name, so the login app's images can never be selected.
#   --ago/--keep  delete tags older than N days, but always keep the newest K of those (rollback targets)
#   --untagged    also delete the image data no tag points to any more; manifests referenced by a still-
#                 tagged image index (per-platform images, SBOM/provenance attestations) are never deleted
# Run it on demand: az acr task run --registry <registry> --name photo-manager-image-cleanup
# ---------------------------------------------------------------------------------------------------
resource "azurerm_container_registry_task" "image_cleanup" {
  count                 = var.registry_cleanup_enabled ? 1 : 0
  name                  = "photo-manager-image-cleanup"
  container_registry_id = local.registry_id
  timeout_in_seconds    = 3600
  tags                  = local.tags

  platform {
    os = "Linux"
  }

  encoded_step {
    context_path = "/dev/null"
    task_content = base64encode(<<-YAML
      version: v1.1.0
      steps:
        - cmd: "acr purge --filter 'photo-(web|worker|migrator):.*' --ago ${var.registry_cleanup_older_than_days}d --keep ${var.registry_cleanup_keep} --untagged${var.registry_cleanup_dry_run ? " --dry-run" : ""}"
          disableWorkingDirectoryOverride: true
          timeout: 3600
    YAML
    )
  }

  timer_trigger {
    name     = "weekly"
    schedule = "0 3 * * 0" # Sundays 03:00 UTC
    enabled  = true
  }
}
