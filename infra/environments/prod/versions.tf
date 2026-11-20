terraform {
  required_version = ">= 1.17.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.14"
    }
    # azapi: only for the managed-certificate binding that azurerm 5.14 cannot express (modules/custom-domain).
    azapi = {
      source  = "azure/azapi"
      version = "~> 2.14"
    }
    # cloudflare: DNS for photo.biffkittz.com (approved 2026-11-19).
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 5.29"
    }
    # temporalcloud: namespace settings and service accounts (approved 2026-11-19).
    temporalcloud = {
      source  = "temporalio/temporalcloud"
      version = "~> 1.13"
    }
  }

  # Remote state in the existing Terraform state storage account, shared with other projects; this project's
  # blobs are photo.* (photo.bootstrap.tfstate holds infra/bootstrap). Entra ID authentication, no storage
  # keys: your account and the CI identities need a Storage Blob Data role on the container (see
  # infra/README.md#state). Backend blocks cannot use variables.
  backend "azurerm" {
    resource_group_name  = "rg-terraform-state"
    storage_account_name = "demonstration"
    container_name       = "terraform"
    key                  = "photo.terraform.tfstate"
    use_azuread_auth     = true
  }
}
