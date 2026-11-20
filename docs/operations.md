# Operations runbook

Resource group `rg-photo-prod` (centralus). Apps: `ca-photo-web`, `ca-photo-worker`; job `caj-photo-migrate`.
Most actions need the Azure CLI (`az login`) and, for Key Vault, your IP temporarily in `admin_ip_ranges`.

## Restarting the apps (after changing secrets)

Secrets are read from Key Vault at startup, so restart after any change:

```bash
for app in ca-photo-web ca-photo-worker; do
  az containerapp revision restart -g rg-photo-prod -n $app \
    --revision $(az containerapp show -g rg-photo-prod -n $app --query properties.latestRevisionName -o tsv)
done
```

## Re-entering secrets

The vaults accept only the apps' private endpoints, so let your machine in, change the secrets, restart, and
close them again:

1. In `infra/environments/prod/terraform.tfvars` set `admin_ip_ranges = ["<your public IP>/32"]`
   (`curl -s https://api.ipify.org`) and `key_vault_admin_object_ids = ["<az ad signed-in-user show --query id -o tsv>"]`,
   export `CLOUDFLARE_API_TOKEN` and `TF_VAR_temporal_api_key` as in deployment step 4, and apply:
   `terraform -chdir=infra/environments/prod apply`. The plan should only open the two vaults' firewalls and add
   your two role assignments per vault. Wait a few minutes for the role assignments to take effect (the script
   says so if they have not yet).
2. Run the script with every secret you want to replace (each `--update` once; `--update all` asks for all of
   them; anything not named is kept):

   ```bash
   scripts/bootstrap-keyvault.sh \
     --web-vault "$(terraform -chdir=infra/environments/prod output -json key_vaults | jq -r .web)" \
     --worker-vault "$(terraform -chdir=infra/environments/prod output -json key_vaults | jq -r .worker)" \
     --update Auth--GitHubClientId --update Auth--GitHubClientSecret
   ```

   Input is hidden. After each value the script prints `received N characters`, so you can tell a paste
   arrived (a GitHub client secret is 40 characters, a client id 20); if the value starts or ends with
   whitespace, which pasting can add, it asks before removing it. An empty entry is asked for again, and
   nothing is stored for a secret that never got a value.
3. Restart both apps ([above](#restarting-the-apps-after-changing-secrets)); the script prints the command.
4. Close the vaults: set both settings back to `[]` and apply again (they must match `TF_VARS_PROD`).

## Rotating keys

| Secret | How | Cadence |
|---|---|---|
| Temporal API keys (web, worker) | Create a new key for the service account in Temporal Cloud, `scripts/bootstrap-keyvault.sh ... --update Temporal--ApiKey` (prompts for the new web key, then the new worker key), restart both apps, then delete the old keys | **Every 90 days** (Temporal guidance); set the key expiry accordingly |
| GitHub OAuth client secret | Generate a second secret in the OAuth app, `--update Auth--GitHubClientSecret`, restart, delete the old secret | Yearly or on suspicion |
| Wasabi keys | Create a new bucket-scoped key, `--update Archive--AccessKeyId` and `--update Archive--SecretAccessKey`, restart worker, revoke old key | Yearly or on suspicion |
| Gemini key | New key in Google AI Studio, `--update Gemini--ApiKey`, restart worker | Yearly or on suspicion |
| Web key | `--rotate-web-key`: the old key becomes `Crypto--WebKeyPrevious` (still decrypts). Restart both apps. New renditions/staging use the new key; regenerate renditions (Actions > Generate missing thumbnails after resetting rendition state, or let them age out) before removing the previous key | On suspicion |
| Payload key | `--rotate-payload-key`, restart both apps. Keep `Temporal--PayloadKeyPrevious` for at least the namespace retention (30 days) so old histories stay readable | On suspicion |
| Data Protection key | Key Vault rotates nothing automatically; create a new version of `dataprotection` if compromised (signs everyone out) | On suspicion |
| Archive password | Not rotatable in place: it protects the whole archive including `protect.ps1` history. Plan a re-encryption project if it is ever exposed | Never routinely |

## Maintenance jobs (Actions page, admins)

| Job | What it does | When |
|---|---|---|
| Re-index archive | Imports objects added outside the app, refreshes changed objects, marks vanished ones `Missing` | Nightly via Temporal Schedule `nightly-reindex` (09:00 UTC); manually after bulk `protect.ps1` runs |
| Generate missing thumbnails | Decrypts archived photos on the workers and creates renditions; also records dimensions, capture date and content hash for legacy photos, and hides byte-identical copies (see [Duplicates](#duplicates)) | After the first re-index; failed photos can be retried by running it again |
| Generate missing descriptions | Gemini descriptions for photos without one. Never replaces a description someone wrote by hand | Optional; consumes Gemini quota (roughly one photo per second across workers, about 80 s each) |

Jobs can be cancelled from the Actions page. Progress is visible there and in the Temporal UI. A job whose
start never reached Temporal (it shows "Queued" with no run) is simply closed by a cancel; a cancel is never
sent to whatever later job reuses the same workflow id, and a run that starts after its job was closed stops
immediately.

### Missing photos and the safety limit

A re-index marks a photo `Missing` when its archive object was not seen during a complete scan. Because a
broken listing (wrong credentials, endpoint or bucket) would otherwise hide the whole library, a scan refuses
to mark more than **10% of the library (at least 3 photos)** missing and reports that in the job summary
instead. `Missing` is reversible: the next scan that sees the object restores the photo.

After deliberately deleting many files from the bucket, start **Re-index archive** with **Mark missing even if
many** ticked. The nightly schedule never forces.

## Duplicates

The same photo can exist more than once in the archive (for example `protect.ps1` run from two machines, each
with its own `photo.db`). When **Generate missing thumbnails** decodes a legacy photo, it computes the
SHA-256 of the decrypted content; if another photo in the library has identical bytes, the newer copy is set to
`Duplicate` and hidden (the earliest stays). Uploads are checked the same way. The database allows only one
ready photo per content hash, so concurrent jobs cannot create duplicates either.

Nothing is deleted: duplicate archive objects stay in the bucket, and their `photo.db` rows (if any) keep
working for `protect.ps1` restores. To list them (break-glass database access, below):

```sql
SELECT original_file_name, archive_key, status_detail FROM photos WHERE status = 'Duplicate' ORDER BY archive_key;
```

Removing them from the bucket is optional and manual. Bucket versioning is on, so a deleted object can be
restored, but prefer leaving them: they cost little and the app already hides them.

Rebuilding the database from the archive (a re-index after losing it) records duplicates the same way: of two
archive files with identical content, one photo is ready and the other is `Duplicate`.

## Downloads and the daily limit

The workers prepare at most **2,000 photos per 24 hours for everyone together** (`Worker:MaxDownloadPhotosPerDay`);
beyond that a download fails with a message saying so. This bounds how fast originals can leave the archive,
even through a compromised web tier ([ADR 0001](adr/0001-split-keys.md)). Each prepared download is logged by
the worker (`Prepared download ... for GitHub user ...`) and every refusal as a warning; both are in Application
Insights traces. To allow a large one-off export, raise the setting on the worker app temporarily:

```bash
az containerapp update -g rg-photo-prod -n ca-photo-worker --set-env-vars Worker__MaxDownloadPhotosPerDay=10000
```

Per download: at most 500 photos and 4 GB (`Worker:MaxPhotosPerDownload`, `Worker:MaxDownloadBytes`, and the
same limits in the API's `Downloads` section, plus 3 downloads in preparation per person).

## Image processing limits

- Only JPEG, PNG, GIF and WebP are decoded, for uploads and for archive files alike. Legacy files in other
  formats are listed but get no thumbnails.
- An upload that passes the quick header check but cannot be fully decoded (corrupt, or not really an image)
  fails with a message on the Uploads page and is **not** archived.
- Each worker decodes at most 2 images at a time (`Imaging:MaxConcurrentDecodes`) and ImageSharp may hold at
  most 1 GB (`Imaging:DecodeMemoryLimitMegabytes`); beyond that a decode fails and Temporal retries it later,
  instead of the container running out of memory. Raise both only together with the worker's memory.

## Sessions

Sign-in sessions end after 8 hours without activity (`Auth:SessionLifetime`) and in any case 24 hours after
signing in with GitHub (`Auth:MaxSessionLifetime`); removing someone from `github_users` ends their session on
their next request. Signing out (or the session ending) in one browser tab ends it in the browser's other tabs
of the app as well, and a tab re-checks the session when it comes back to the foreground. The GitHub sign-in
callback accepts at most 30 requests a minute per IP address.

### Sign-in problems

- "That GitHub account is not allowed to use Photo Manager." means GitHub identified an account that is not in
  `github_users` (recorded as `auth.denied` in the audit trail). Check which account the browser is signed in to
  on github.com, and the numeric id: `curl -s https://api.github.com/users/<login> | jq .id`. The ids the app
  uses: `az containerapp show -g rg-photo-prod -n ca-photo-web --query "properties.template.containers[0].env[?starts_with(name, 'Auth__Users')]" -o table`.
- "Signing in with GitHub did not complete." is any other failure. The web app logs the reason as a warning:
  `az containerapp logs show -g rg-photo-prod -n ca-photo-web --tail 200 | grep "GitHub sign-in failed"`.
  Typical reasons: `redirect_uri_mismatch` (the production OAuth app's callback must be exactly
  `https://photo.biffkittz.com/signin-github`), `incorrect_client_credentials` (the client id or secret in the
  web vault is not the production OAuth app's: update them with `scripts/bootstrap-keyvault.sh --update
  Auth--GitHubClientSecret`, then restart the web app), `access_denied` (cancelled on GitHub), an expired or
  invalid `oauth state` (the sign-in took more than 15 minutes), or `Correlation failed` with
  `'__Host-pm-correlation…' cookie not found` (the browser did not send back the cookie set when the sign-in
  started: cookies blocked for the site, or the sign-in started on a different host name than
  `photo.biffkittz.com`; releases before 2026-11-20 also had a cookie path bug that caused it every time).

## Container registry storage

Images live in the registry **`crloginappdemo7fyguj`** (`rg-loginapp-demo`), shared with the login app. Every
release pushes three images there (`photo-web`, `photo-worker`, `photo-migrator`, roughly 100 MB each, plus SBOM
and provenance records).

A weekly clean-up keeps that bounded: the ACR task **`photo-manager-image-cleanup`** (created by
`infra/bootstrap`) runs every Sunday at 03:00 UTC and

- deletes this project's image tags that are older than **30 days**, but always keeps the **10 newest** of those
  per repository, so recent releases stay available for rollback (tags younger than 30 days are never touched);
- then deletes image data that no tag refers to any more. Anything a still-tagged image refers to (its
  platform manifests, SBOM and provenance records) is kept;
- only ever matches the three `photo-*` repositories (the repository filter must match the whole name); the
  login app's images are never selected.

```bash
az acr task run  --registry crloginappdemo7fyguj --name photo-manager-image-cleanup     # run it now
az acr task logs --registry crloginappdemo7fyguj --name photo-manager-image-cleanup     # what the last run deleted
```

To change the policy, edit `registry_cleanup_older_than_days` / `registry_cleanup_keep` in `infra/bootstrap`
and apply it. To preview a change, set `registry_cleanup_dry_run = true` first: runs then only log what they
would delete. `registry_cleanup_enabled = false` removes the task.

## Monitoring and alerting

- **Application Insights** (`appi-photo-prod`): requests, failures, dependencies, traces, and Temporal SDK
  metrics (meter `PhotoManager.Temporal`).
- **Log Analytics** (`log-photo-prod`): container console logs, Key Vault audit events (`AuditEvent`),
  PostgreSQL logs.
- **Temporal Cloud**: workflow failures, schedule-to-start latency, action usage.
- **Audit trail**: table `audit_events` (sign-ins, denied sign-ins, uploads, downloads, jobs).

Suggested alerts (Temporal worker alerting guidance): pollers at zero for 5 minutes; activity
schedule-to-start p99 above 30 minutes; workflow task failures (non-determinism); any `auth.denied` burst;
Key Vault secret reads by an unexpected identity; 5xx rate on the web app; any `refused: ... photos were prepared
in the last 24 hours` warning from the worker (the daily download limit was reached).

### Why did an activity fail?

The Temporal UI shows only the error **type** of a failed activity (`DbUpdateException`, `GeminiTransient`, ...);
the message reads "Encoded failure" because every failure is encrypted with the payload key before it reaches
Temporal Cloud. The readable reason is in the worker's console log. The live stream (`az containerapp logs show`)
keeps only the last few hundred lines; Log Analytics keeps everything:

```bash
ws=$(az monitor log-analytics workspace show -g rg-photo-prod -n log-photo-prod --query customerId -o tsv)
az monitor log-analytics query -w "$ws" --timespan PT6H -o table --analytics-query '
  ContainerAppConsoleLogs_CL
  | where ContainerAppName_s == "ca-photo-worker" and Log_s has_any ("warn:", "fail:", "Exception")
  | where not(Log_s startswith "   at ")
  | summarize hits = count(), latest = max(TimeGenerated) by line = substring(Log_s, 0, 250)
  | order by hits desc | take 20'
```

`GeminiTransient` means Gemini rate-limited the request (429) or was overloaded (503, "This model is currently
experiencing high demand"). Those are retried with growing pauses (up to 8 attempts); photos still without a
description afterwards are picked up by the next **Generate missing descriptions** run. If it persists for hours,
run the job later, or check your Gemini quota.

## Error reporting

Server errors go to Application Insights (stack traces stay server-side; clients get a ProblemDetails body
with a trace id). Browser errors are shown to the user as a notification and are **not** sent anywhere:
`app/frontend/src/plugins/errorHandler.ts` has a `setErrorReporter` hook for a service such as Sentry, left
unwired on purpose because error context can include photo names and descriptions. If you wire one, scrub
URLs and messages, disable session replay, and note the processor in this document.

## Disaster recovery

| Scenario | Recovery |
|---|---|
| Database lost/corrupted | Point-in-time restore (14 days) of the flexible server; or create an empty database, run the migration job and **Re-index archive**: v2 photos recover filenames and descriptions from their sealed metadata, legacy photos from their object keys. Then regenerate renditions |
| Renditions lost or corrupted | A rendition that is missing, corrupted or encrypted with a retired web key is regenerated automatically the next time someone views it. For a bulk rebuild, reset the state (`UPDATE photos SET rendition_state = 'NotStarted'`) and run **Generate missing thumbnails** |
| Blob staging lost | Only in-flight uploads are affected: they fail and show **Retry** on the Upload page; if the staged copy is gone, the user is asked to upload the file again |
| Key Vault deleted | Soft delete + purge protection (90 days): `az keyvault recover` |
| App down / bad release | Roll back the image tag (docs/deployment.md#ongoing-releases-cicd) |
| Need a photo without the app | `photo-crypt decrypt` with the archive password (docs/encryption.md#offline-tool) |

Keep an offline copy of the archive password and the web key (password manager). Without the archive
password, the archive is unrecoverable by design.

## Break-glass database access

PostgreSQL has no public endpoint and no password logins. To inspect data, add your Entra user to
`postgres_admin_principals`, apply, and connect from inside the VNet, for example with a one-off Container Apps
job running `psql` with an Entra access token (`az account get-access-token --resource-type oss-rdbms`). Remove
the administrator afterwards.

## Temporal workflow changes

Running workflows replay their history on new code. Any change to workflow logic (not activities) must be
replay-compatible or guarded with `Workflow.Patched("...")`. Short-lived workflows and frequent
continue-as-new keep the exposure small; drain long-running jobs before deploying risky changes.
