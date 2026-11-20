# Infrastructure (Terraform)

Everything in Azure, the Cloudflare DNS records and the Temporal Cloud namespace settings are managed here.
Step-by-step first deployment: [docs/deployment.md](../docs/deployment.md).

```
infra/
  bootstrap/            one-time: resource groups, app and GitHub OIDC identities, their access to shared resources
  environments/prod/    the production composition (remote state, providers, module wiring)
  modules/              reusable building blocks, one concern each (network, key-vault, storage, ...)
  .terraform-docs.yml   generates the Inputs/Outputs sections of every README
```

| Directory | What it creates | README |
|---|---|---|
| `bootstrap` | `rg-photo-prod`; the web/worker/migrator identities with registry pull access; the custom blob role the apps get; deploy/plan identities (in `rg-photo-cicd`) and their roles, including scoped access to the shared state and registry | [bootstrap/README.md](bootstrap/README.md) |
| `environments/prod` | Everything else (about a dozen module calls) | [environments/prod/README.md](environments/prod/README.md) |
| `modules/network` | VNet, subnets, NSGs, private DNS zones | [README](modules/network/README.md) |
| `modules/observability` | Log Analytics, Application Insights (Entra-only ingestion) | [README](modules/observability/README.md) |
| `modules/key-vault` | RBAC Key Vault, private endpoint, roles, diagnostics (used twice: web, worker) | [README](modules/key-vault/README.md) |
| `modules/storage` | Private storage account, containers, lifecycle, container-scoped roles | [README](modules/storage/README.md) |
| `modules/postgres` | PostgreSQL flexible server, Entra-only, VNet-integrated | [README](modules/postgres/README.md) |
| `modules/container-apps-environment` | Container Apps environment in the VNet | [README](modules/container-apps-environment/README.md) |
| `modules/container-app` | One Container App (web, worker) | [README](modules/container-app/README.md) |
| `modules/migration-job` | Container Apps Job running the migrator | [README](modules/migration-job/README.md) |
| `modules/custom-domain` | Cloudflare records, managed certificate and binding | [README](modules/custom-domain/README.md) |
| `modules/temporal-cloud` | Namespace settings and service accounts | [README](modules/temporal-cloud/README.md) |

## Providers

Pinned in each root's `versions.tf` (pessimistic `~>` constraints) and locked in `.terraform.lock.hcl`
(committed). Only these providers are approved for this project; ask before adding another one.

| Provider | Version | Why |
|---|---|---|
| `hashicorp/azurerm` | ~> 5.14 | All Azure resources (preferred) |
| `azure/azapi` | ~> 2.14 | Only for gaps in azurerm: binding the managed certificate to the Container App hostname |
| `cloudflare/cloudflare` | ~> 5.29 | DNS records for `photo.biffkittz.com` |
| `temporalio/temporalcloud` | ~> 1.13 | Namespace settings and service accounts in account `joqqs` |

## State

Both configurations keep their state in the **existing** Terraform state storage, shared with other projects:

| Setting | Value |
|---|---|
| Resource group / storage account / container | `rg-terraform-state` / `demonstration` / `terraform` |
| `infra/bootstrap` state | `photo.bootstrap.tfstate` |
| `infra/environments/prod` state | `photo.terraform.tfstate` |
| Authentication | Entra ID (`use_azuread_auth = true`), never storage account keys |

The backend blocks live in each `versions.tf` (backend blocks cannot use variables; the bootstrap's
`state_*` variables must match them). Nothing else is needed: `terraform init` just works.

Who can touch the state:

- **You** (workstation): *Storage Blob Data Contributor* on the `terraform` container, granted once by hand
  (command in [docs/deployment.md](../docs/deployment.md#0-before-you-start)); it has to exist before the
  bootstrap can even initialize.
- **Deploy identity** (approved applies): custom role `photo-prod-terraform-state-writer`, **Plan identity**
  (plans, run with `-lock=false`): custom role `photo-prod-terraform-state-reader`. Both are defined by the
  bootstrap in the state account's resource group and hold blob data actions only (plus reading container
  properties): unlike the built-in *Storage Blob Data* roles they cannot delete or reconfigure the container,
  which no assignment condition could prevent. Both assignments carry an ABAC condition that limits them to
  blobs whose names start with `photo.terraform.tfstate` (`state_blob_prefix`), so CI can neither read nor
  change other projects' state, nor the bootstrap's own `photo.bootstrap.tfstate`. If Azure ever rejects these
  conditions for the account, set `scope_state_access_to_prefix = false` in the bootstrap (container-wide
  access) and consider a dedicated container instead.

State still contains sensitive attributes that providers export (for example the app storage account's access
keys, unusable because shared-key access is disabled), which is why access to it is this narrow.

## Container registry

Images go to the existing registry **`crloginappdemo7fyguj`** in `rg-loginapp-demo`, shared with the login app
(`container_registry_name` in both configurations, `container_registry_resource_group` in the bootstrap). This
project's repositories are `photo-web`, `photo-worker` and `photo-migrator`.

- Neither configuration creates, changes or reads the registry resource itself (only the clean-up task below
  is added to it). Both refer to it by name: a data
  source would copy the registry's admin password (if the admin user is enabled) into this project's state. The
  login server is assumed to be `<name>.azurecr.io`; set `container_registry_login_server` if it differs.
- The deploy identity has *AcrPush* on the registry and no right to grant or remove roles there. The bootstrap
  (run by you) creates the web, worker and migrator identities and gives them *AcrPull*; the production
  configuration only looks the identities up by name and never touches the registry.
- Both of those built-in roles apply to the whole registry, including the login app's repositories.
  Repository-level permissions would require switching the registry to ABAC repository permissions, which
  changes how the login app's existing role assignments work, so that was not done. See
  [docs/security.md](../docs/security.md) (residual risks).
- `infra/bootstrap` adds one ACR task to the registry, `photo-manager-image-cleanup`: every Sunday it deletes
  this project's image tags older than 30 days except the 10 newest, then the image data no tag refers to. Its
  repository filter (`photo-(web|worker|migrator)`) must match whole repository names, so it never selects the
  login app's images ([docs/operations.md](../docs/operations.md#container-registry-storage)).

## Conventions

- Each module has `main.tf`, `variables.tf`, `outputs.tf` (and `versions.tf` when it needs a non-hashicorp
  provider). Every variable and output has a description and a type; secrets are `sensitive = true`.
- Names follow the Azure abbreviations (`rg-`, `kv-`, `ca-`, `caj-`, `id-`, `psql-`, `st`, `cr`), with a
  short deterministic suffix where names must be globally unique.
- Stateful resources (PostgreSQL, Key Vaults, Temporal namespace) have `prevent_destroy`. Container images are
  ignored by Terraform (`ignore_changes`) because releases update them.
- **No secret values in Terraform.** Key Vault is populated by `scripts/bootstrap-keyvault.sh`; Temporal API
  keys are created in Temporal Cloud. See [State](#state) for who can read the state.
- Never commit state, plans or `terraform.tfvars` (see `infra/.gitignore`); the `.example` files are the
  templates.

## Validating and documenting changes

```bash
terraform fmt -recursive infra
for d in infra/bootstrap infra/environments/prod; do
  terraform -chdir=$d init -backend=false -input=false && terraform -chdir=$d validate
done
# Regenerate the Inputs/Outputs tables in every README after changing variables or outputs:
for d in bootstrap environments/prod modules/*; do
  (cd infra && docker run --rm -u "$(id -u):$(id -g)" -v "$PWD:/infra" \
    quay.io/terraform-docs/terraform-docs:0.20.0 --config /infra/.terraform-docs.yml "/infra/$d")
done
```

CI runs `fmt -check` and `validate` on every pull request; pull requests touching `infra/` also get a
`terraform plan` from the read-only identity (skipped for Dependabot, which cannot use the repository's
secrets), and merges to `main` apply after approval in the `production` environment
(`.github/workflows/terraform.yml`).

## Order of operations (first deployment)

1. Grant yourself *Storage Blob Data Contributor* on the state container (once).
2. `infra/bootstrap`: `terraform apply` (Owner on the subscription; it also adds role assignments on the
   shared registry in `rg-loginapp-demo`).
3. GitHub: variables, secrets and the `production` environment from the bootstrap outputs.
4. Deploy workflow with `images_only` to push the first images.
5. `infra/environments/prod`: `terraform apply` with `image_tag` set to those images and **zero replicas**
   (`web_min_replicas = 0`, `worker_replicas = 0`) plus your IP and object id for Key Vault access.
6. Temporal Cloud API keys, then `scripts/bootstrap-keyvault.sh` for both vaults.
7. Run the migration job (schema and database roles).
8. Set replicas to 1, remove your Key Vault access, apply again; sign in; run **Re-index archive**.

Details for each step, including the exact commands: [docs/deployment.md](../docs/deployment.md).

## Known provider quirks

- **Cloudflare TXT records (provider 5.x).** Cloudflare may return TXT content wrapped in quotes. If the
  `asuid` verification record (`modules/custom-domain`) shows a diff on every plan, or the first apply fails
  with "Provider produced inconsistent result", set its `content` to `"\"${var.verification_id}\""` (quoted),
  which is what the API stores. Verify on the first apply; it cannot be tested without the real account.
- **Custom domain validation right after the DNS records are created.** Azure checks the `asuid` TXT record
  as soon as the hostname is added; if Cloudflare is not serving the new record yet, the apply fails with
  `InvalidCustomHostNameValidation: A TXT record ... was not found`, and resolvers may remember "not found" for
  the zone's negative TTL (30 minutes). The module waits 60 s after creating the records to avoid this; if it
  still happens, wait up to 30 minutes and apply again (the records themselves are already in place).
- **Managed certificate on teardown.** `azapi_update_resource` does not unbind the certificate when destroyed.
  Before setting `manage_dns = false` (or destroying), unbind the hostname first:
  `az containerapp hostname delete -g rg-photo-prod -n ca-photo-web --hostname photo.biffkittz.com`.
