terraform {
  required_version = ">= 1.17.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.14"
    }
  }

  # State lives in the existing Terraform state storage, next to the main configuration's state
  # (keys photo.bootstrap.tfstate and photo.terraform.tfstate). Entra ID authentication: no storage keys.
  # Backend blocks cannot use variables; keep these values in sync with var.state_* (see infra/README.md#state).
  backend "azurerm" {
    resource_group_name  = "rg-terraform-state"
    storage_account_name = "demonstration"
    container_name       = "terraform"
    key                  = "photo.bootstrap.tfstate"
    use_azuread_auth     = true
  }
}

provider "azurerm" {
  features {}
  subscription_id     = var.subscription_id
  storage_use_azuread = true
}
