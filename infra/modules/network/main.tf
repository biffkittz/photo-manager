# Private networking (docs/adr/0003-private-networking.md):
#   snet-apps       /23  Container Apps environment (delegated)
#   snet-postgres   /28  PostgreSQL flexible server (delegated, VNet-integrated: no public endpoint)
#   snet-endpoints  /27  private endpoints for Key Vault and Blob Storage
locals {
  apps_prefix      = cidrsubnet(var.address_space, 7, 0)   # x.x.0.0/23
  postgres_prefix  = cidrsubnet(var.address_space, 12, 32) # x.x.2.0/28
  endpoints_prefix = cidrsubnet(var.address_space, 11, 24) # x.x.3.0/27
}

resource "azurerm_virtual_network" "this" {
  name                = "vnet-${var.name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  address_space       = [var.address_space]
  tags                = var.tags
}

resource "azurerm_subnet" "apps" {
  name                 = "snet-apps"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [local.apps_prefix]

  delegation {
    name = "container-apps"
    service_delegation {
      name    = "Microsoft.App/environments"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

resource "azurerm_subnet" "postgres" {
  name                 = "snet-postgres"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [local.postgres_prefix]

  delegation {
    name = "postgres-flexible"
    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

resource "azurerm_subnet" "endpoints" {
  name                              = "snet-endpoints"
  resource_group_name               = var.resource_group_name
  virtual_network_name              = azurerm_virtual_network.this.name
  address_prefixes                  = [local.endpoints_prefix]
  private_endpoint_network_policies = "Enabled" # lets the NSG below apply to private endpoints
}

# Defence in depth: only the apps subnet may reach the database and the private endpoints. (The apps
# subnet itself is left to Container Apps, which manages its ingress.)
resource "azurerm_network_security_group" "postgres" {
  name                = "nsg-${var.name}-postgres"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags

  security_rule {
    name                       = "allow-apps-postgres"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_address_prefix      = local.apps_prefix
    source_port_range          = "*"
    destination_address_prefix = local.postgres_prefix
    destination_port_range     = "5432"
  }

  # The delegated subnet's own traffic (server management, replication if high availability is ever
  # enabled) must stay allowed; the deny rule below would otherwise block it.
  security_rule {
    name                       = "allow-postgres-subnet-internal"
    priority                   = 110
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "*"
    source_address_prefix      = local.postgres_prefix
    source_port_range          = "*"
    destination_address_prefix = local.postgres_prefix
    destination_port_range     = "*"
  }

  security_rule {
    name                       = "deny-other-vnet-inbound"
    priority                   = 4000
    direction                  = "Inbound"
    access                     = "Deny"
    protocol                   = "*"
    source_address_prefix      = "VirtualNetwork"
    source_port_range          = "*"
    destination_address_prefix = "*"
    destination_port_range     = "*"
  }
}

resource "azurerm_network_security_group" "endpoints" {
  name                = "nsg-${var.name}-endpoints"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags

  security_rule {
    name                       = "allow-apps-https"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_address_prefix      = local.apps_prefix
    source_port_range          = "*"
    destination_address_prefix = local.endpoints_prefix
    destination_port_range     = "443"
  }

  security_rule {
    name                       = "deny-other-vnet-inbound"
    priority                   = 4000
    direction                  = "Inbound"
    access                     = "Deny"
    protocol                   = "*"
    source_address_prefix      = "VirtualNetwork"
    source_port_range          = "*"
    destination_address_prefix = "*"
    destination_port_range     = "*"
  }
}

resource "azurerm_subnet_network_security_group_association" "postgres" {
  subnet_id                 = azurerm_subnet.postgres.id
  network_security_group_id = azurerm_network_security_group.postgres.id
}

resource "azurerm_subnet_network_security_group_association" "endpoints" {
  subnet_id                 = azurerm_subnet.endpoints.id
  network_security_group_id = azurerm_network_security_group.endpoints.id
}

# Private DNS: names of the private endpoints / VNet-integrated server resolve to private IPs inside the VNet.
resource "azurerm_private_dns_zone" "this" {
  for_each            = toset(["privatelink.vaultcore.azure.net", "privatelink.blob.core.windows.net", "${var.name}.private.postgres.database.azure.com"])
  name                = each.value
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "this" {
  for_each             = azurerm_private_dns_zone.this
  name                 = "link-${azurerm_virtual_network.this.name}"
  private_dns_zone_id  = each.value.id
  virtual_network_id   = azurerm_virtual_network.this.id
  registration_enabled = false
  tags                 = var.tags
}
