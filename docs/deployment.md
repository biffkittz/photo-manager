# Deployment guide

Target: Azure Container Apps in **centralus**, site at **https://photo.biffkittz.com** (DNS at Cloudflare),
Temporal Cloud namespace **photo.joqqs**, archive bucket **photo-bk** at Wasabi us-east-2.

Infrastructure is Terraform (`infra/`); application releases are GitHub Actions (`.github/workflows/`).
Secrets are never in Terraform or GitHub variables: they go into Key Vault with `scripts/bootstrap-keyvault.sh`.
Run every command below from the **repository root** unless a step says otherwise.

## Overview

| Step | Where | What |
|---|---|---|
| 0. Before you start | Workstation, accounts | Tools, access and the values you will need |
| 1. Bootstrap | Workstation (as subscription Owner) | Resource groups, identities, roles |
| 2. GitHub configuration | GitHub settings | Environment, variables, secrets |
| 3. First images | GitHub Actions (Deploy, *images only*) | Images the apps are created from |
| 4. Infrastructure | Workstation | Everything else, apps at zero replicas |
| 5. Secrets | Workstation (IP allowed in step 4) | Key Vault contents |
| 6. Database | Workstation (`az`) | Schema and database roles |
| 7. Start the apps | Workstation, then GitHub settings | Replicas on, vaults closed, `TF_VARS_PROD` |
| 8. Verify | Browser | |

From then on every change goes through pull requests and GitHub Actions ([Ongoing releases](#ongoing-releases-cicd)).

## 0. Before you start

Tools on your workstation: Azure CLI (`az login` done), Terraform 1.17 or later 1.x (CI uses 1.17.1), and `jq`.

Accounts and access:

- Azure: an account with **Owner** on the subscription. The bootstrap and the first apply (steps 4 and 7)
  create role assignments, which needs Owner (or *User Access Administrator* in addition to *Contributor*).
  The Terraform state account and the shared container registry must be **in the same subscription** as Photo
  Manager: the bootstrap builds their resource ids from `subscription_id`. Select it first:

  ```bash
  az account set --subscription <subscription-id>
  ```

- The existing Terraform state storage: storage account **`demonstration`** in **`rg-terraform-state`**,
  container **`terraform`**. This project's state files are `photo.bootstrap.tfstate` and
  `photo.terraform.tfstate`. Terraform authenticates to it with **Entra ID** (no storage keys), so your
  account needs a data role on the container once (role assignments take a few minutes to apply):

  ```bash
  az role assignment create --assignee "$(az ad signed-in-user show --query id -o tsv)" \
    --role "Storage Blob Data Contributor" \
    --scope "$(az storage account show -g rg-terraform-state -n demonstration --query id -o tsv)/blobServices/default/containers/terraform"
  ```

- The existing container registry **`crloginappdemo7fyguj`** in **`rg-loginapp-demo`** (shared with the login
  app). This project adds the repositories `photo-web`, `photo-worker` and `photo-migrator`. If the registry
  restricts network access, it must accept pushes from GitHub-hosted runners and pulls from the Container Apps
  environment.
- Cloudflare: an API token with *Zone > DNS > Edit* limited to the `biffkittz.com` zone, and a second token
  with *Zone > DNS > Read* for plans.
- Temporal Cloud:
  - an API key with the **Account Admin** role for Terraform (it adopts the namespace and creates the apps'
    service accounts): the `TF_VAR_temporal_api_key` you already have;
  - an API key of a service account with the account role **Read**, for plans;
  - the region of the `photo.joqqs` namespace (Temporal Cloud UI > Namespaces > photo, for example
    `aws-us-east-2`);
  - **API key authentication enabled on that namespace** (same page). The apps connect to the namespace
    endpoint `photo.joqqs.tmprl.cloud:7233` with API keys, which Temporal recommends for all namespaces.
- GitHub: admin on `biffkittz/photo-manager`, and this code on its `main` branch (step 3 builds from it).
- A **production** GitHub OAuth app: Homepage `https://photo.biffkittz.com`, callback
  `https://photo.biffkittz.com/signin-github`. Its client id and secret go into Key Vault in step 5.
- At hand for step 5: the archive password (`protect.ps1`), the Wasabi access key id and secret, the Gemini API
  key and the OAuth app's client id and secret. For step 2: the Six Labors license file (`sixlabors.lic`).

Recommended before going live: create a **dedicated Wasabi access key** limited to the `photo-bk` bucket
(a sub-user with a bucket-scoped policy) instead of the account root key, and keep **bucket versioning** on
(it is: deleted or overwritten archive objects stay recoverable). See [security.md](security.md).

## 1. Bootstrap (once, from your workstation)

Creates the app resource group, the app's three managed identities (web, worker, migrator) with their *AcrPull*
on the shared registry, and the two GitHub OIDC identities (deploy, plan) in their own resource group
`rg-photo-cicd`, with least-privilege roles:

- Both CI identities can use the production state blob (`photo.terraform.tfstate`) in the shared `terraform`
  container and nothing else in it, not even this bootstrap's own `photo.bootstrap.tfstate`. They get custom
  roles (`photo-prod-terraform-state-writer`/`-reader`) that cannot delete or reconfigure the container.
- On the shared registry the deploy identity gets *AcrPush* only; it cannot grant or remove roles there. The
  only thing created in the registry is the weekly clean-up task for this project's images
  ([operations.md](operations.md#container-registry-storage)).
- The custom role `photo-prod-blob-data-writer` (read, write and delete blobs, no container rights) is what the
  production configuration gives the apps on their storage containers.
- The plan identity trusts pull requests and the `main` branch: on `main` it plans before an apply is approved.

```bash
cp infra/bootstrap/terraform.tfvars.example infra/bootstrap/terraform.tfvars   # set subscription_id
terraform -chdir=infra/bootstrap init
terraform -chdir=infra/bootstrap apply
terraform -chdir=infra/bootstrap output -json                                  # values for step 2
```

`terraform.tfvars` only needs `subscription_id`; everything else has defaults (the registry, the state account,
names). Set `container_registry_login_server` only if the registry's login server is not
`crloginappdemo7fyguj.azurecr.io`.

If Azure rejects the storage role assignments' conditions (`scope_state_access_to_prefix`), set that variable
to `false`: the identities then get the role on the whole `terraform` container. See
[infra/README.md](../infra/README.md#state).

**Skip this on a first deployment.** Only if you applied the production configuration before 2026-11-20 (it
created the app identities itself back then), move them to the bootstrap before applying either configuration
again. These commands change only Terraform's records, not Azure:

```bash
for t in web worker migrator; do
  terraform -chdir=infra/environments/prod state rm "azurerm_user_assigned_identity.app[\"$t\"]" "azurerm_role_assignment.acr_pull[\"$t\"]"
  terraform -chdir=infra/bootstrap import "azurerm_user_assigned_identity.app[\"$t\"]" \
    "/subscriptions/<subscription-id>/resourceGroups/rg-photo-prod/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-photo-prod-$t"
done
```

If the bootstrap apply then reports that an *AcrPull* assignment already exists, delete the old one and apply
again: `az role assignment delete --role AcrPull --assignee <identity client id> --scope <registry id>`.

## 2. GitHub repository configuration

*Settings > Environments*: create **production**; add yourself as a **required reviewer** (every apply and
deploy then waits for approval) and add a deployment branch rule that allows only `main`. The workflows also
refuse to run production jobs for other branches, but a branch can edit its own copy of a workflow, so this
rule is what actually enforces it.

*Settings > Secrets and variables > Actions*:

| Kind | Name | Value |
|---|---|---|
| Variable | `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_CLIENT_ID_DEPLOY`, `AZURE_CLIENT_ID_PLAN` | `github_actions` in the bootstrap output |
| Variable | `ACR_NAME`, `ACR_LOGIN_SERVER` | same output: `crloginappdemo7fyguj` and `crloginappdemo7fyguj.azurecr.io` |
| Variable | `AZURE_RESOURCE_GROUP` | `rg-photo-prod` |
| Variable | `SITE_HOSTNAME` | `photo.biffkittz.com` |
| Variable | `TF_VARS_PROD` | **in step 7**, not now: the final `infra/environments/prod/terraform.tfvars` |
| Repository secret | `CLOUDFLARE_API_TOKEN_READONLY` | Cloudflare token with *Zone > DNS > Read* on the zone (plans) |
| Repository secret | `TEMPORAL_CLOUD_API_KEY_READONLY` | API key of a Temporal Cloud service account with the account role **Read** (plans) |
| **production** environment secret | `CLOUDFLARE_API_TOKEN` | the DNS-edit token (apply only) |
| **production** environment secret | `TEMPORAL_CLOUD_API_KEY` | the Account Admin Temporal Cloud API key Terraform manages the namespace with (apply only) |
| **production** environment secret | `SIXLABORS_LICENSE` | contents of `sixlabors.lic` (release image builds) |

Write-capable credentials are **environment** secrets, so GitHub releases them only to jobs that run in the
approved `production` environment. Plans (pull requests, and the `plan-main` job on `main`) get only the
read-only repository secrets. The Terraform backend needs no configuration in GitHub: it is in `versions.tf`.

Also enable *Code security*: Dependabot alerts/updates, secret scanning with **push protection**, and
CodeQL (the workflow is included). Protect `main` (require pull requests and the CI checks).

**Until step 7 is done**, pushes to `main` start runs that cannot succeed yet: a *Deploy* run (after CI) and, when
`infra/` changed, a *Terraform* run whose `plan-main` job fails without `TF_VARS_PROD`. **Reject** their
approval requests; the first apply is done from your workstation in step 4. The only run to approve before
then is the images-only deployment of step 3.

## 3. First images

The Container Apps need an image to exist before Terraform creates them:

*Actions > Deploy > Run workflow*, branch **main**, with **images_only** checked, then approve the run. It
builds and pushes the three images and skips everything else. The run summary shows the image tag:
`sha-` followed by the first 12 characters of the commit (`git rev-parse --short=12 origin/main` gives the
same). You need it in step 4.

## 4. Infrastructure (first apply: apps scaled to zero)

The apps cannot run until their secrets (step 5) and the database (step 6) exist, so the first apply creates
them with **zero replicas**. Otherwise they would crash-loop, and the apply could fail waiting for a healthy
revision. Run this from the machine you will use in step 5 (its public IP is let into the Key Vaults).

```bash
cp infra/environments/prod/terraform.tfvars.example infra/environments/prod/terraform.tfvars
#   set: subscription_id, image_tag (step 3; required), github_users, temporal_namespace_region,
#        admin_ip_ranges = ["<your public IP>/32"]   (curl -s https://api.ipify.org),
#        key_vault_admin_object_ids = ["<az ad signed-in-user show --query id -o tsv>"],
#        web_min_replicas = 0, worker_replicas = 0   (the example file already has these four as placeholders)
export CLOUDFLARE_API_TOKEN=...      # the DNS-edit token
export TF_VAR_temporal_api_key=...   # the Account Admin Temporal Cloud API key
terraform -chdir=infra/environments/prod init
terraform -chdir=infra/environments/prod plan -out tfplan     # review it: everything is created, plus 1 import (the Temporal namespace)
terraform -chdir=infra/environments/prod apply tfplan
```

Azure access comes from your `az login`. Keep those two environment variables in the shell for step 7.

Notes:

- `temporal_import_existing_namespace = true` (default) adopts the existing `photo.joqqs` namespace and turns
  on delete protection. `temporal_namespace_region` must match it exactly (regions cannot change).
- The managed TLS certificate is issued after the DNS records exist; the apply waits for it (a few minutes).
  If the Cloudflare TXT record shows a perpetual diff because of quoting, see [infra/README.md](../infra/README.md#known-provider-quirks).
- `admin_ip_ranges` and `key_vault_admin_object_ids` let *you* populate Key Vault in the next step; both are
  removed again in step 7.

## 5. Secrets (Key Vault)

1. Temporal Cloud UI > *Settings > Service accounts*: create an API key for **photo-prod-web** and one for
   **photo-prod-worker** (Terraform created both accounts in step 4; their ids:
   `terraform -chdir=infra/environments/prod output temporal_service_account_ids`). Set a 90-day expiry.
2. Populate both vaults, from the machine whose IP is in `admin_ip_ranges`. Role assignments from step 4 can
   take a few minutes; the script checks access to both vaults first and stops if they have not propagated
   yet (run it again a little later).

```bash
scripts/bootstrap-keyvault.sh \
  --web-vault "$(terraform -chdir=infra/environments/prod output -json key_vaults | jq -r .web)" \
  --worker-vault "$(terraform -chdir=infra/environments/prod output -json key_vaults | jq -r .worker)"
```

It asks, with hidden input, for: the archive password, the Wasabi access key id and secret, the Gemini API key,
the two Temporal service-account API keys (worker, web) and the GitHub OAuth app's client id and secret.
(Each can instead come from an environment variable; see `scripts/bootstrap-keyvault.sh --help`.) It generates
the web key, the Temporal payload key and the Data Protection key itself and stores them in the right vaults.
Values are never printed. Running it again only fills in what is missing.

## 6. Database

The migration job applies the schema and creates the least-privilege PostgreSQL roles for the web and worker
identities. It needs no secrets, but must finish before the apps start:

```bash
az containerapp job start -g rg-photo-prod -n caj-photo-migrate
az containerapp job execution list -g rg-photo-prod -n caj-photo-migrate -o table   # wait for Succeeded
```

The deploy workflow runs it automatically on every later release.

## 7. Start the apps

In `infra/environments/prod/terraform.tfvars` set `web_min_replicas = 1` and `worker_replicas = 1`, set
`admin_ip_ranges = []` (vaults back to private-endpoint-only) and `key_vault_admin_object_ids = []` (unless you
want to keep Key Vault admin rights), then apply again from the same shell:

```bash
terraform -chdir=infra/environments/prod apply
```

Now create the `TF_VARS_PROD` GitHub variable (step 2) with the contents of this final `terraform.tfvars`. It
holds no secrets (keys come from environment variables) and must not contain settings the configuration no
longer has, such as `container_registry_resource_group` (Terraform warns about undeclared variables). From now
on, infrastructure changes go through pull requests and the *Terraform* workflow; if you ever apply from your
workstation again, keep your local file identical to `TF_VARS_PROD`.

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
  main[Merge to main] --> ci2[CI] --> newest[Still the newest commit on main?] --> deploy{{Deploy: approval}}
  deploy --> push[Build release images + SBOM/provenance, push to ACR]
  push --> migrate[Migration job] --> worker[Roll out worker, wait until healthy] --> web[Roll out web, wait until healthy] --> smoke[Smoke test]
  main --> planmain[Terraform plan, shown in the run summary] --> apply{{Terraform apply: approval, only the same plan}}
```

- Images are tagged `sha-<commit>`; Terraform ignores image changes, so infrastructure applies never roll
  back a release.
- The deploy waits for each new revision to report *Running* and *Healthy* (the previous revision keeps
  serving until then); a revision that fails to start fails the workflow.
- **Rollback:** `az containerapp update -g rg-photo-prod -n ca-photo-web --image <acr>/photo-web:sha-<previous>`
  (same for the worker). Migrations are forward-only; write backwards-compatible migrations (expand, then
  contract in a later release).
- Temporal workflow code changes must stay replay-compatible (see `app/backend/CLAUDE.md`).
- Terraform on `main`: the **plan-main** job plans with the read-only identity and puts the full plan in the run
  summary *before* the **apply** job asks for approval. Read it, then approve. The apply job plans again with the
  deploy identity and applies only if that plan makes exactly the same changes; if Azure or the state changed
  in between, it stops without applying and you re-run the workflow to review the new plan.
- Changing only `TF_VARS_PROD` starts nothing (the workflow runs when `infra/` changes on `main`): run
  *Actions > Terraform > Run workflow* on `main` to apply it.
- Only the newest commit on `main` is deployed: a run whose commit is no longer the tip of `main` is skipped
  before approval and fails after approval (the newer commit is deployed by its own run). Roll back deliberately
  as described above, never by re-running an old deployment.

## Cost notes (approximate, per month)

Container Apps (web 0.5 vCPU min 1 + worker 1 vCPU min 1, mostly idle) ~$15-30; PostgreSQL B1ms ~$15;
private endpoints (2 vaults + blob) ~$22; Log Analytics/App Insights depends on volume (~$5); Key Vault and
storage cents. The images add storage to the shared registry (see
[operations.md](operations.md#container-registry-storage)). Temporal Cloud and Gemini are billed separately.
