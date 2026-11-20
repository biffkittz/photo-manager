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

Jobs can be cancelled from the Actions page. Progress is visible there and in the Temporal UI.

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

## Sessions

Sign-in sessions end after 8 hours without activity (`Auth:SessionLifetime`) and in any case 24 hours after
signing in with GitHub (`Auth:MaxSessionLifetime`); removing someone from `github_users` ends their session on
their next request.

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
Key Vault secret reads by an unexpected identity; 5xx rate on the web app.

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
