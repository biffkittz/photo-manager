data "azurerm_client_config" "current" {}

locals {
  name                = "${var.project}-${var.environment}"
  suffix              = var.name_suffix != "" ? var.name_suffix : substr(sha1("${var.subscription_id}-${var.project}"), 0, 6)
  resource_group_name = var.resource_group_name != "" ? var.resource_group_name : "rg-${local.name}"

  # The existing, shared container registry, referenced by name: a data source would copy its admin password
  # (if the admin user is enabled) into this configuration's state.
  registry_id           = "/subscriptions/${var.subscription_id}/resourceGroups/${var.container_registry_resource_group}/providers/Microsoft.ContainerRegistry/registries/${var.container_registry_name}"
  registry_login_server = var.container_registry_login_server != "" ? var.container_registry_login_server : "${lower(var.container_registry_name)}.azurecr.io"
  temporal_namespace    = "${var.temporal_namespace_name}.${var.temporal_account_id}"
  tags = merge(var.tags, {
    project     = var.project
    environment = var.environment
    managed-by  = "terraform"
  })

  images = {
    for target in ["web", "worker", "migrator"] :
    target => "${local.registry_login_server}/photo-${target}:${var.image_tag}"
  }

  # Passwordless connections: the username is the identity's PostgreSQL role, the password an Entra token.
  connection_strings = {
    for tier, identity in azurerm_user_assigned_identity.app :
    tier => "Host=${module.postgres.fqdn};Port=5432;Database=${module.postgres.database_name};Username=${identity.name};SslMode=VerifyFull"
  }

  # Application Insights has local authentication disabled, so its connection string is not a credential
  # (ingestion needs an Entra token); passing it as a plain environment variable is safe.
  app_insights_connection_string = nonsensitive(module.observability.application_insights_connection_string)

  common_env = {
    ASPNETCORE_ENVIRONMENT                = "Production"
    APPLICATIONINSIGHTS_CONNECTION_STRING = local.app_insights_connection_string
    Database__UseEntraAuthentication      = "true"
    Blob__ServiceUri                      = module.storage.blob_endpoint
    Temporal__Address                     = var.temporal_grpc_endpoint
    Temporal__Namespace                   = local.temporal_namespace
  }

  allowed_users_env = merge([
    for index, user in var.github_users : {
      "Auth__Users__${index}__GitHubId" = tostring(user.id)
      "Auth__Users__${index}__Login"    = user.login
      "Auth__Users__${index}__Role"     = user.role
    }
  ]...)
}

data "azurerm_resource_group" "app" {
  name = local.resource_group_name
}

# ---------------------------------------------------------------------------------------------------
# Identities: one per tier, so each gets only the rights its tier needs.
# ---------------------------------------------------------------------------------------------------
resource "azurerm_user_assigned_identity" "app" {
  for_each            = toset(["web", "worker", "migrator"])
  name                = "id-${local.name}-${each.key}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  tags                = local.tags
}

# Pull access to the shared registry (the deploy identity may grant AcrPull there, and only that; see
# infra/bootstrap).
resource "azurerm_role_assignment" "acr_pull" {
  for_each             = azurerm_user_assigned_identity.app
  scope                = local.registry_id
  role_definition_name = "AcrPull"
  principal_id         = each.value.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "telemetry" {
  for_each             = { for tier in ["web", "worker"] : tier => azurerm_user_assigned_identity.app[tier] }
  scope                = module.observability.application_insights_id
  role_definition_name = "Monitoring Metrics Publisher"
  principal_id         = each.value.principal_id
  principal_type       = "ServicePrincipal"
}

# ---------------------------------------------------------------------------------------------------
# Platform
# ---------------------------------------------------------------------------------------------------
module "network" {
  source              = "../../modules/network"
  name                = local.name
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  address_space       = var.vnet_address_space
  tags                = local.tags
}

module "observability" {
  source              = "../../modules/observability"
  name                = local.name
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  retention_in_days   = var.log_retention_days
  tags                = local.tags
}

# Web vault: web key, payload key, the API's Temporal key, GitHub OAuth app, Data Protection wrapping key.
module "key_vault_web" {
  source                      = "../../modules/key-vault"
  name                        = "kv-${var.project}-web-${local.suffix}"
  resource_group_name         = data.azurerm_resource_group.app.name
  location                    = var.location
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  private_endpoint_subnet_id  = module.network.endpoints_subnet_id
  private_dns_zone_id         = module.network.key_vault_dns_zone_id
  admin_ip_ranges             = var.admin_ip_ranges
  admin_object_ids            = var.key_vault_admin_object_ids
  secret_reader_principal_ids = { web = azurerm_user_assigned_identity.app["web"].principal_id }
  crypto_user_principal_ids   = { web = azurerm_user_assigned_identity.app["web"].principal_id }
  log_analytics_workspace_id  = module.observability.log_analytics_workspace_id
  tags                        = local.tags
}

# Worker vault: archive password, web key, payload key, the workers' Temporal key, Wasabi and Gemini keys.
module "key_vault_worker" {
  source                      = "../../modules/key-vault"
  name                        = "kv-${var.project}-wrk-${local.suffix}"
  resource_group_name         = data.azurerm_resource_group.app.name
  location                    = var.location
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  private_endpoint_subnet_id  = module.network.endpoints_subnet_id
  private_dns_zone_id         = module.network.key_vault_dns_zone_id
  admin_ip_ranges             = var.admin_ip_ranges
  admin_object_ids            = var.key_vault_admin_object_ids
  secret_reader_principal_ids = { worker = azurerm_user_assigned_identity.app["worker"].principal_id }
  log_analytics_workspace_id  = module.observability.log_analytics_workspace_id
  tags                        = local.tags
}

module "storage" {
  source                     = "../../modules/storage"
  name                       = "st${var.project}${var.environment}${local.suffix}"
  resource_group_name        = data.azurerm_resource_group.app.name
  location                   = var.location
  private_endpoint_subnet_id = module.network.endpoints_subnet_id
  private_dns_zone_id        = module.network.blob_dns_zone_id
  tags                       = local.tags

  # Least privilege per container: the web tier can write staged uploads and its key ring but only read
  # renditions and prepared downloads; the workers produce renditions and downloads.
  container_role_assignments = {
    web_staging        = { container = "staging", principal_id = azurerm_user_assigned_identity.app["web"].principal_id, role = "Storage Blob Data Contributor" }
    web_renditions     = { container = "renditions", principal_id = azurerm_user_assigned_identity.app["web"].principal_id, role = "Storage Blob Data Reader" }
    web_downloads      = { container = "downloads", principal_id = azurerm_user_assigned_identity.app["web"].principal_id, role = "Storage Blob Data Reader" }
    web_dataprotection = { container = "dataprotection", principal_id = azurerm_user_assigned_identity.app["web"].principal_id, role = "Storage Blob Data Contributor" }
    worker_staging     = { container = "staging", principal_id = azurerm_user_assigned_identity.app["worker"].principal_id, role = "Storage Blob Data Contributor" }
    worker_renditions  = { container = "renditions", principal_id = azurerm_user_assigned_identity.app["worker"].principal_id, role = "Storage Blob Data Contributor" }
    worker_downloads   = { container = "downloads", principal_id = azurerm_user_assigned_identity.app["worker"].principal_id, role = "Storage Blob Data Contributor" }
  }
}

module "postgres" {
  source                     = "../../modules/postgres"
  name                       = "psql-${local.name}-${local.suffix}"
  resource_group_name        = data.azurerm_resource_group.app.name
  location                   = var.location
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  delegated_subnet_id        = module.network.postgres_subnet_id
  private_dns_zone_id        = module.network.postgres_dns_zone_id
  postgres_version           = var.postgres_version
  sku_name                   = var.postgres_sku
  log_analytics_workspace_id = module.observability.log_analytics_workspace_id
  tags                       = local.tags

  entra_administrators = merge(
    {
      migrator = {
        object_id      = azurerm_user_assigned_identity.app["migrator"].principal_id
        principal_name = azurerm_user_assigned_identity.app["migrator"].name
        principal_type = "ServicePrincipal"
      }
    },
    { for index, admin in var.postgres_admin_principals : "admin-${index}" => admin },
  )
}

module "container_apps_environment" {
  source                     = "../../modules/container-apps-environment"
  name                       = local.name
  resource_group_name        = data.azurerm_resource_group.app.name
  location                   = var.location
  infrastructure_subnet_id   = module.network.apps_subnet_id
  log_analytics_workspace_id = module.observability.log_analytics_workspace_id
  tags                       = local.tags
}

# ---------------------------------------------------------------------------------------------------
# Applications
# ---------------------------------------------------------------------------------------------------
module "web_app" {
  source              = "../../modules/container-app"
  name                = "ca-${var.project}-web"
  resource_group_name = data.azurerm_resource_group.app.name
  environment_id      = module.container_apps_environment.id
  identity_id         = azurerm_user_assigned_identity.app["web"].id
  registry_server     = local.registry_login_server
  image               = local.images["web"]
  cpu                 = 0.5
  memory              = "1Gi"
  min_replicas        = var.web_min_replicas
  max_replicas        = var.web_max_replicas
  ingress             = { target_port = 8080 }
  tags                = local.tags

  env = merge(local.common_env, local.allowed_users_env, {
    Azure__ManagedIdentityClientId = azurerm_user_assigned_identity.app["web"].client_id
    Azure__KeyVaultUri             = module.key_vault_web.vault_uri
    Database__ConnectionString     = local.connection_strings["web"]
    DataProtection__KeyVaultKeyId  = "${module.key_vault_web.vault_uri}keys/dataprotection"
  })

  # The image pull and first start need these permissions in place.
  depends_on = [azurerm_role_assignment.acr_pull, module.key_vault_web, module.storage]
}

module "worker_app" {
  source                           = "../../modules/container-app"
  name                             = "ca-${var.project}-worker"
  resource_group_name              = data.azurerm_resource_group.app.name
  environment_id                   = module.container_apps_environment.id
  identity_id                      = azurerm_user_assigned_identity.app["worker"].id
  registry_server                  = local.registry_login_server
  image                            = local.images["worker"]
  cpu                              = 1
  memory                           = "2Gi"
  min_replicas                     = var.worker_replicas
  max_replicas                     = max(1, var.worker_replicas) # Container Apps requires a maximum of at least 1
  ingress                          = null
  termination_grace_period_seconds = 60 # > Worker:GracefulShutdownTimeout (25 s)
  tags                             = local.tags

  env = merge(local.common_env, {
    Azure__ManagedIdentityClientId = azurerm_user_assigned_identity.app["worker"].client_id
    Azure__KeyVaultUri             = module.key_vault_worker.vault_uri
    Database__ConnectionString     = local.connection_strings["worker"]
    Archive__ServiceUrl            = var.wasabi_service_url
    Archive__Region                = var.wasabi_region
    Archive__BucketName            = var.wasabi_bucket
    Gemini__Model                  = var.gemini_model
    Worker__AiActivitiesPerSecond  = tostring(var.gemini_requests_per_second)
  })

  depends_on = [azurerm_role_assignment.acr_pull, module.key_vault_worker, module.storage]
}

module "migration_job" {
  source              = "../../modules/migration-job"
  name                = "caj-${var.project}-migrate"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  environment_id      = module.container_apps_environment.id
  identity_id         = azurerm_user_assigned_identity.app["migrator"].id
  registry_server     = local.registry_login_server
  image               = local.images["migrator"]
  tags                = local.tags

  env = {
    DOTNET_ENVIRONMENT               = "Production"
    Azure__ManagedIdentityClientId   = azurerm_user_assigned_identity.app["migrator"].client_id
    Database__ConnectionString       = local.connection_strings["migrator"]
    Database__UseEntraAuthentication = "true"
    Migrator__AppRoles__0__Name      = azurerm_user_assigned_identity.app["web"].name
    Migrator__AppRoles__0__ObjectId  = azurerm_user_assigned_identity.app["web"].principal_id
    Migrator__AppRoles__0__Kind      = "Web"
    Migrator__AppRoles__1__Name      = azurerm_user_assigned_identity.app["worker"].name
    Migrator__AppRoles__1__ObjectId  = azurerm_user_assigned_identity.app["worker"].principal_id
    Migrator__AppRoles__1__Kind      = "Worker"
  }

  depends_on = [azurerm_role_assignment.acr_pull, module.postgres]
}

# ---------------------------------------------------------------------------------------------------
# External services
# ---------------------------------------------------------------------------------------------------
module "custom_domain" {
  source               = "../../modules/custom-domain"
  count                = var.manage_dns ? 1 : 0
  hostname             = var.custom_domain
  cloudflare_zone_name = var.cloudflare_zone_name
  container_app_id     = module.web_app.id
  container_app_fqdn   = module.web_app.fqdn
  verification_id      = module.web_app.custom_domain_verification_id
  environment_id       = module.container_apps_environment.id
}

module "temporal_cloud" {
  source                 = "../../modules/temporal-cloud"
  count                  = var.manage_temporal_cloud ? 1 : 0
  namespace_name         = var.temporal_namespace_name
  region                 = var.temporal_namespace_region
  retention_days         = var.temporal_retention_days
  service_account_prefix = local.name
}
