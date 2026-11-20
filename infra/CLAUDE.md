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
  its keys into state). CI gets custom roles (blob data actions only, never container write/delete) limited by
  ABAC to `photo.terraform.tfstate*`, never the bootstrap's own state (`infra/bootstrap`, docs/adr/0009).
- The container registry is the owner's existing, **shared** `crloginappdemo7fyguj` (`rg-loginapp-demo`). Never
  create, modify or read it with a data source (admin password into state); refer to it by name. Role assignments
  on it (this project's identities only) live in `infra/bootstrap`, never in `environments/prod`, and CI gets no
  right to assign roles there; repositories are `photo-*` (docs/adr/0011).
- The app identities (web, worker, migrator) are created by `infra/bootstrap` (`prevent_destroy`: database roles
  are bound to their object ids); `environments/prod` reads them with a data source. Apps get blob write access
  only through the bootstrap's custom `<project>-<environment>-blob-data-writer` role, never the built-in
  *Storage Blob Data Contributor* (container delete, and undelete of soft-deleted blobs).
- Production applies run only the plan shown before approval (`.github/workflows/terraform.yml`, plan
  fingerprint script in `.github/scripts`); keep the plan identity able to plan everything an apply plans.
- Data services stay private (no public network access, no shared keys, Entra-only PostgreSQL). Storage
  containers are managed through ARM (`storage_account_id`); the provider runs with
  `data_plane_available = false`.
- Keep `prevent_destroy` on stateful resources and `ignore_changes` on Container App images.
- Every module: `main.tf`, `variables.tf`, `outputs.tf` (+ `versions.tf` for non-hashicorp providers), a
  description and type on every variable/output, validation blocks where inputs have a fixed shape.
- After changing variables/outputs, regenerate README tables with terraform-docs (command in `infra/README.md`).
- Changes must stay idempotent: a second `apply` must be a no-op.
