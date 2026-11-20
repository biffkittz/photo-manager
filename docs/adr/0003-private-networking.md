# 0003. Private networking for data services

- Status: Accepted (2026-11-19, chosen by the project owner)

## Context

PostgreSQL, Key Vault and Blob Storage hold metadata, secrets and encrypted photos. Public endpoints with
firewall rules and credentials are the default but leave every service one misconfiguration away from the
internet.

## Decision

- One VNet (`10.40.0.0/16`): Container Apps environment subnet (`/23`), delegated PostgreSQL subnet (`/28`),
  private endpoint subnet (`/27`), each with an NSG that only admits the apps subnet.
- PostgreSQL flexible server is VNet-integrated (no public endpoint) with **Entra-only** authentication;
  apps connect with their managed identities.
- Key Vaults and the storage account have public network access disabled and private endpoints with private
  DNS zones; shared-key access is disabled on storage.
- Operators reach Key Vault temporarily through `admin_ip_ranges`; Terraform manages storage containers
  through Azure Resource Manager (`storage_account_id`, `data_plane_available = false`), so CI never needs
  network access to data planes.

## Consequences

- No passwords or storage keys exist to leak; data services are unreachable from the internet.
- About $22/month for private endpoints; break-glass database access needs a VNet-connected job.
- First-time Key Vault population needs the temporary IP allowance (documented in docs/deployment.md).
