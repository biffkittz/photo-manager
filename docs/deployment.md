# Deployment guide

Target: Azure Container Apps in **centralus**, site at **https://photo.biffkittz.com** (DNS at Cloudflare),
Temporal Cloud namespace **photo.joqqs**, archive bucket **photo-bk** at Wasabi us-east-2.

Infrastructure is Terraform (`infra/`); application releases are GitHub Actions (`.github/workflows/`).
Secrets are never in Terraform or GitHub variables: they go into Key Vault with `scripts/bootstrap-keyvault.sh`.
Run every command below from the **repository root** unless a step says otherwise.

## 0. Before you start

You need:

- Azure: an account with **Owner** on the subscription (only for the one-time bootstrap) and `az login` done.
- The existing Terraform state storage: storage account **`demonstration`** in **`rg-terraform-state`**,
  container **`terraform`**. This project's state files are `photo.bootstrap.tfstate` and
  `photo.terraform.tfstate`. Terraform authenticates to it with **Entra ID** (no storage keys), so your
  account needs a data role on the container once (role assignments take a few minutes to apply):

  ```bash
  az role assignment create --assignee "$(az ad signed-in-user show --query id -o tsv)" \
    --role "Storage Blob Data Contributor" \
    --scope "$(az storage account show -g rg-terraform-state -n demonstration --query id -o tsv)/blobServices/default/containers/terraform"
  ```

- Cloudflare: an API token with *Zone > DNS > Edit* limited to the `biffkittz.com` zone (plus, for pull-request
  plans, a second token with *Zone > DNS > Read*).
- Temporal Cloud: an account-level API key for Terraform (the `TF_VAR_temporal_api_key` you already have), the
  region of the `photo.joqqs` namespace (Temporal Cloud UI > Namespaces > photo, e.g. `aws-us-east-1`), and
  **API key authentication enabled on that namespace** (same page). The apps connect to the namespace
  endpoint `photo.joqqs.tmprl.cloud:7233` with API keys, which Temporal recommends for all namespaces.
- GitHub: admin on `biffkittz/photo-manager`.
- A **production** GitHub OAuth app: Homepage `https://photo.biffkittz.com`,
  callback `https://photo.biffkittz.com/signin-github`.
- The Six Labors license (`sixlabors.lic`), the archive password, Wasabi keys and the Gemini key.

Recommended before going live: create a **dedicated Wasabi access key** limited to the `photo-bk` bucket
(a sub-user with a bucket-scoped policy) instead of the account root key, and keep **bucket versioning** on
(it is: deleted or overwritten archive objects stay recoverable). See [security.md](security.md).

## 1. Bootstrap (once, from your workstation)

Creates the app resource group, the container registry, and the two GitHub OIDC identities (deploy, plan)
in their own resource group `rg-photo-cicd`, with least-privilege roles. Both identities get access to this
project's state blobs (`photo.*`) in the shared `terraform` container and nothing else in it. The bootstrap's
own state goes to `photo.bootstrap.tfstate`.

```bash
cp infra/bootstrap/terraform.tfvars.example infra/bootstrap/terraform.tfvars   # set subscription_id
terraform -chdir=infra/bootstrap init
terraform -chdir=infra/bootstrap apply
terraform -chdir=infra/bootstrap output -json                                  # values for the next steps
```

If Azure rejects the storage role assignments' conditions (`scope_state_access_to_prefix`), set that variable
to `false`: the identities then get the role on the whole `terraform` container. See
[infra/README.md](../infra/README.md#state).

## 2. GitHub repository configuration

*Settings > Environments*: create **production**; add yourself as a **required reviewer** (every apply and
deploy then waits for approval) and restrict it to the `main` branch.

*Settings > Secrets and variables > Actions*:

| Kind | Name | Value |
|---|---|---|
| Variable | `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_CLIENT_ID_DEPLOY`, `AZURE_CLIENT_ID_PLAN` | `terraform output github_actions` (bootstrap) |
| Variable | `ACR_LOGIN_SERVER` | `terraform output container_registry_login_server` |
| Variable | `AZURE_RESOURCE_GROUP` | `rg-photo-prod` |
| Variable | `SITE_HOSTNAME` | `photo.biffkittz.com` |
| Variable | `TF_VARS_PROD` | contents of your `infra/environments/prod/terraform.tfvars` (no secrets in it). Add it after step 4, once the file has its final `image_tag` and replica counts |
| Repository secret | `CLOUDFLARE_API_TOKEN_READONLY` | Cloudflare token with *Zone > DNS > Read* on the zone (pull-request plans) |
| Repository secret | `TEMPORAL_CLOUD_API_KEY_READONLY` | API key of a Temporal Cloud service account with the account role **Read** (pull-request plans) |
| **production** environment secret | `CLOUDFLARE_API_TOKEN` | the DNS-edit token (apply only) |
| **production** environment secret | `TEMPORAL_CLOUD_API_KEY` | the Temporal Cloud API key Terraform manages the namespace with (apply only) |
| **production** environment secret | `SIXLABORS_LICENSE` | contents of `sixlabors.lic` (release image builds) |

Write-capable credentials are **environment** secrets, so GitHub releases them only to jobs that run in the
approved `production` environment; a pull request (even one that edits a workflow) can reach only the
read-only ones. The Terraform backend needs no configuration in GitHub: it is in `versions.tf`.

Also enable *Code security*: Dependabot alerts/updates, secret scanning with **push protection**, and
CodeQL (the workflow is included). Protect `main` (require pull requests and the CI checks).

## 3. First images

The Container Apps need an image to exist before Terraform creates them:

*Actions > Deploy > Run workflow* with **images_only** checked. Note the tag it prints (`sha-<12 chars>`).

## 4. Infrastructure (first apply: apps scaled to zero)

The apps cannot run until their secrets (step 5) and the database (step 6) exist, so the first apply creates
them with **zero replicas**. Otherwise they would crash-loop, and the apply could fail waiting for a healthy
revision.

```bash
cp infra/environments/prod/terraform.tfvars.example infra/environments/prod/terraform.tfvars
#   set: image_tag (step 3), github_users, temporal_namespace_region, admin_ip_ranges = ["<your-ip>/32"],
#        key_vault_admin_object_ids = ["<az ad signed-in-user show --query id -o tsv>"],
#        web_min_replicas = 0, worker_replicas = 0
export CLOUDFLARE_API_TOKEN=...      # DNS-edit token
export TF_VAR_temporal_api_key=...   # Temporal Cloud API key
terraform -chdir=infra/environments/prod init
terraform -chdir=infra/environments/prod plan -out tfplan     # review it: everything is created, plus 1 import (the Temporal namespace)
terraform -chdir=infra/environments/prod apply tfplan
```

Notes:

- `temporal_import_existing_namespace = true` (default) adopts the existing `photo.joqqs` namespace and turns
  on delete protection. `temporal_namespace_region` must match it exactly (regions cannot change).
- The managed TLS certificate is issued after the DNS records exist; the apply waits for it (a few minutes).
  If the Cloudflare TXT record shows a perpetual diff because of quoting, see [infra/README.md](../infra/README.md#known-provider-quirks).
- `admin_ip_ranges` and `key_vault_admin_object_ids` let *you* populate Key Vault in the next step; both are
  removed again afterwards.

## 5. Secrets (Key Vault)

1. Temporal Cloud UI > *Settings > Service accounts*: create an API key for **photo-prod-web** and one for
   **photo-prod-worker** (`terraform -chdir=infra/environments/prod output temporal_service_account_ids`).
   Set a 90-day expiry.
2. Populate both vaults (values are prompted with hidden input, or taken from environment variables; the
   script checks access to both vaults first and stops if a role assignment has not propagated yet):

```bash
scripts/bootstrap-keyvault.sh \
  --web-vault "$(terraform -chdir=infra/environments/prod output -json key_vaults | jq -r .web)" \
  --worker-vault "$(terraform -chdir=infra/environments/prod output -json key_vaults | jq -r .worker)"
```

## 6. Database

The migration job applies the schema and creates the least-privilege PostgreSQL roles for the web and worker
identities. It must finish before the apps start:

```bash
az containerapp job start -g rg-photo-prod -n caj-photo-migrate
az containerapp job execution list -g rg-photo-prod -n caj-photo-migrate -o table   # wait for Succeeded
```

The deploy workflow runs it automatically on every later release.

## 7. Start the apps

In `terraform.tfvars` set `web_min_replicas = 1` and `worker_replicas = 1`, remove your IP from
`admin_ip_ranges` (vaults back to private-endpoint-only) and empty `key_vault_admin_object_ids` (unless you want
to keep Key Vault admin rights), then apply again:

```bash
terraform -chdir=infra/environments/prod apply
```

Now copy the final `terraform.tfvars` into the `TF_VARS_PROD` GitHub variable (step 2).

## 8. Verify

- https://photo.biffkittz.com/healthz and `/readyz` return 200; the browser shows a valid certificate.
- Sign in with GitHub; a non-allowlisted account gets "not allowed".
- *Actions* page: run **Re-index archive** (imports the existing `protect.ps1` photos), then **Generate missing
  thumbnails** (this also detects byte-identical copies in the archive and hides them), then optionally
  **Generate missing descriptions** (uses Gemini quota).
- Temporal Cloud UI shows workflows with **encrypted** payloads.
- Application Insights receives requests and Temporal metrics.

## Ongoing releases (CI/CD)

```mermaid
flowchart LR
  pr[Pull request] --> ci[CI: build, tests, E2E, image scan, gitleaks, terraform validate] --> codeql[CodeQL]
  pr --> plan[Terraform plan (read-only identity, -lock=false)]
  main[Merge to main] --> ci2[CI] --> deploy{{Deploy: approval}}
  deploy --> push[Build release images + SBOM/provenance, push to ACR]
  push --> migrate[Migration job] --> worker[Roll out worker, wait until healthy] --> web[Roll out web, wait until healthy] --> smoke[Smoke test]
  main --> apply{{Terraform apply: approval}}
```

- Images are tagged `sha-<commit>`; Terraform ignores image changes, so infrastructure applies never roll
  back a release.
- The deploy waits for each new revision to report *Running* and *Healthy* (the previous revision keeps
  serving until then); a revision that fails to start fails the workflow.
- **Rollback:** `az containerapp update -g rg-photo-prod -n ca-photo-web --image <acr>/photo-web:sha-<previous>`
  (same for the worker). Migrations are forward-only; write backwards-compatible migrations (expand, then
  contract in a later release).
- Temporal workflow code changes must stay replay-compatible (see `app/backend/CLAUDE.md`).
- The apply job plans and applies in one approved run. Before approving, compare with the pull request's plan;
  if anything changed in Azure meanwhile (drift), the apply's plan output shows it.

## Cost notes (approximate, per month)

Container Apps (web 0.5 vCPU min 1 + worker 1 vCPU min 1, mostly idle) ~$15-30; PostgreSQL B1ms ~$15;
private endpoints (2 vaults + blob) ~$22; Container Registry Basic ~$5 (10 GiB included, see
[operations.md](operations.md#container-registry-storage)); Log Analytics/App Insights depends on volume (~$5);
Key Vault and storage cents. Temporal Cloud and Gemini are billed separately.
