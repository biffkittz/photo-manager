# 0009. Terraform state in the existing shared state account

- Status: Accepted (2026-11-19, requested by the project owner; supersedes the bootstrap-created state account)

## Context

The first version of `infra/bootstrap` created its own state storage account and kept the bootstrap's state
locally. The owner already runs a Terraform state store for other projects: storage account `demonstration`
in `rg-terraform-state`, container `terraform`, and asked for this project to use it.

## Decision

- Both configurations use the existing account: `photo.bootstrap.tfstate` and `photo.terraform.tfstate` in the
  `terraform` container, configured directly in each `versions.tf` (no backend config files to copy around).
- Authentication is **Entra ID only** (`use_azuread_auth = true`). Using storage keys would require CI to hold
  `listKeys` on the account, which means full access to every project's state in it.
- The CI identities receive data roles on the container limited by an ABAC condition to blobs named
  `photo.*`: the deploy identity can read and write, the pull-request plan identity can only read (plans run
  with `-lock=false`). A variable can drop the condition if Azure rejects it for this account.
- The operator grants themself *Storage Blob Data Contributor* on the container once, by hand, because the
  bootstrap's own state lives there.
- The CI identities move to their own resource group (`rg-photo-cicd`), outside the app resource group the
  deploy identity manages.

## Consequences

- No local state files and no separately managed state account.
- Other projects' state in the shared container is out of reach for this project's CI.
- The ABAC condition cannot be tested without the real account; the first `terraform init` in CI confirms it.
