# CLAUDE.md: infra (Terraform)

Read the root `CLAUDE.md` first. Overview and conventions: `infra/README.md`; deployment steps:
`docs/deployment.md`.

## Rules

- **Never run `terraform plan/apply/import` against real accounts, or anything that needs Azure, Cloudflare
  or Temporal Cloud credentials, without explicit approval from the user.** Offline checks are fine:
  `terraform fmt -recursive infra` and `terraform -chdir=<root> init -backend=false && terraform validate`.
- Approved providers only: azurerm (preferred), azapi (only for azurerm gaps), cloudflare, temporalcloud.
  Ask before adding a provider or an external module. Pin with `~>` and keep `.terraform.lock.hcl` committed.
- No secret values in Terraform (no `azurerm_key_vault_secret`, no Temporal API key resources, no generated
  passwords); mark sensitive variables/outputs `sensitive = true`.
- State lives in the owner's **shared** state account (`rg-terraform-state`/`demonstration`, container `terraform`,
  this project's blobs `photo.*.tfstate`), Entra auth only, configured inline in each `versions.tf`. Never read,
  write or list other projects' blobs there; never add a data source that reads that account (it would copy
  its keys into state). CI access is scoped to `photo.*` by ABAC (`infra/bootstrap`, docs/adr/0009).
- Data services stay private (no public network access, no shared keys, Entra-only PostgreSQL). Storage
  containers are managed through ARM (`storage_account_id`); the provider runs with
  `data_plane_available = false`.
- Keep `prevent_destroy` on stateful resources and `ignore_changes` on Container App images.
- Every module: `main.tf`, `variables.tf`, `outputs.tf` (+ `versions.tf` for non-hashicorp providers), a
  description and type on every variable/output, validation blocks where inputs have a fixed shape.
- After changing variables/outputs, regenerate README tables with terraform-docs (command in `infra/README.md`).
- Changes must stay idempotent: a second `apply` must be a no-op.
