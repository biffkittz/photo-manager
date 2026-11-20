# 0009. Terraform state in the existing shared state account

- Status: Accepted (2026-11-19, requested by the project owner; supersedes the bootstrap-created state account).
  Amended 2026-11-20 after a security review: custom roles, and CI limited to the production state.

## Context

The first version of `infra/bootstrap` created its own state storage account and kept the bootstrap's state
locally. The owner already runs a Terraform state store for other projects: storage account `demonstration`
in `rg-terraform-state`, container `terraform`, and asked for this project to use it.

## Decision

- Both configurations use the existing account: `photo.bootstrap.tfstate` and `photo.terraform.tfstate` in the
  `terraform` container, configured directly in each `versions.tf` (no backend config files to copy around).
- Authentication is **Entra ID only** (`use_azuread_auth = true`). Using storage keys would require CI to hold
  `listKeys` on the account, which means full access to every project's state in it.
- The CI identities receive roles on the container limited by an ABAC condition to blobs whose names start with
  `photo.terraform.tfstate` (the production state and any workspace states named after it): the deploy
  identity can read and write, the plan identity can only read (plans run with `-lock=false`). The bootstrap's
  own `photo.bootstrap.tfstate` does not match, so CI can neither read it nor plant resources in it that the
  operator's next Owner-level apply would then offer to destroy. A variable can drop the condition if Azure
  rejects it for this account.
- The roles are **custom** (`photo-prod-terraform-state-writer`/`-reader`, defined in the state account's resource
  group) with blob data actions only, plus reading container properties. The built-in *Storage Blob Data*
  roles also grant `containers/write` and `containers/delete`, which no assignment condition limits: with them,
  a compromised pipeline could delete the whole shared container and every project's state in it.
- The operator grants themself *Storage Blob Data Contributor* on the container once, by hand, because the
  bootstrap's own state lives there.
- The CI identities move to their own resource group (`rg-photo-cicd`), outside the app resource group the
  deploy identity manages.

## Consequences

- No local state files and no separately managed state account.
- Other projects' state in the shared container, and the container itself, are out of reach for this project's
  CI; so is the bootstrap's own state.
- The ABAC condition cannot be tested without the real account; the first `terraform init` in CI confirms it.
