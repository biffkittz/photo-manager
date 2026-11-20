provider "azurerm" {
  subscription_id     = var.subscription_id
  storage_use_azuread = true

  features {
    key_vault {
      purge_soft_delete_on_destroy    = false
      recover_soft_deleted_key_vaults = true
    }

    resource_group {
      prevent_deletion_if_contains_resources = true
    }

    # The storage account has no public endpoint, so its data plane is unreachable from CI runners and
    # workstations. Containers and lifecycle rules are managed through Azure Resource Manager instead
    # (storage_account_id), and this stops the provider from ever attempting a data-plane call.
    storage {
      data_plane_available = false
    }
  }
}

provider "azapi" {
  subscription_id = var.subscription_id
}

# Authenticates with the CLOUDFLARE_API_TOKEN environment variable (a token scoped to Zone:DNS:Edit on
# the biffkittz.com zone).
provider "cloudflare" {}

# Uses var.temporal_api_key (TF_VAR_temporal_api_key) or the TEMPORAL_CLOUD_API_KEY environment variable.
# allowed_account_id makes Terraform refuse to touch any other Temporal Cloud account.
provider "temporalcloud" {
  api_key            = var.temporal_api_key
  allowed_account_id = var.temporal_account_id
}
