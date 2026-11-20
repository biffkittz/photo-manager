# Private blob storage for web-key-encrypted data (staged uploads, renditions, prepared downloads) and the
# ASP.NET Core Data Protection key ring. No public network access, no account keys (Entra only), and
# least-privilege role assignments scoped to individual containers.
resource "azurerm_storage_account" "this" {
  name                              = var.name
  resource_group_name               = var.resource_group_name
  location                          = var.location
  account_kind                      = "StorageV2"
  account_tier                      = "Standard"
  account_replication_type          = var.replication_type
  min_tls_version                   = "TLS1_2"
  https_traffic_only_enabled        = true
  shared_access_key_enabled         = false
  default_to_oauth_authentication   = true
  allow_nested_items_to_be_public   = false
  cross_tenant_replication_enabled  = false
  infrastructure_encryption_enabled = true
  public_network_access             = "Disabled"
  tags                              = var.tags

  blob_properties {
    delete_retention_policy {
      days = 7
    }

    container_delete_retention_policy {
      days = 7
    }
  }

  network_rules {
    default_action = "Deny"
    bypass         = ["None"]
  }
}

# Created through the management plane (storage_account_id), so Terraform never needs data-plane access.
resource "azurerm_storage_container" "this" {
  for_each              = toset(var.containers)
  name                  = each.value
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}

resource "azurerm_storage_management_policy" "this" {
  storage_account_id = azurerm_storage_account.this.id

  rule {
    name    = "expire-transient-blobs"
    enabled = true

    filters {
      blob_types   = ["blockBlob"]
      prefix_match = [for c in var.expiring_containers : "${c}/"]
    }

    actions {
      base_blob {
        delete_after_days_since_modification_greater_than = 1
      }
    }
  }
}

resource "azurerm_private_endpoint" "blob" {
  name                = "pe-${var.name}-blob"
  resource_group_name = var.resource_group_name
  location            = var.location
  subnet_id           = var.private_endpoint_subnet_id
  tags                = var.tags

  private_service_connection {
    name                           = "psc-${var.name}-blob"
    private_connection_resource_id = azurerm_storage_account.this.id
    subresource_names              = ["blob"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [var.private_dns_zone_id]
  }
}

resource "azurerm_role_assignment" "containers" {
  for_each             = var.container_role_assignments
  scope                = azurerm_storage_container.this[each.value.container].id
  role_definition_name = each.value.role
  principal_id         = each.value.principal_id
  principal_type       = "ServicePrincipal"
}
