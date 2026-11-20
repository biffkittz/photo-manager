# 0011. Images in the existing shared container registry

- Status: Accepted (2026-11-19, requested by the project owner; supersedes the bootstrap-created registry).
  Amended 2026-11-20 after a security review: no role-assignment rights for CI on the registry.

## Context

`infra/bootstrap` originally created a dedicated Basic registry. The owner asked to use an existing registry
instead: `crloginappdemo7fyguj` in `rg-loginapp-demo`, which already serves the login app.

## Decision

- Images are pushed to the existing registry as `photo-web`, `photo-worker` and `photo-migrator`.
- Terraform never creates, changes or reads the registry resource; it refers to it by name and resource group
  (a data source would store the registry's admin password, if enabled, in this project's state).
- Access uses Entra role assignments only (no admin user, no tokens): the deploy identity gets *AcrPush*; the
  web, worker and migrator identities get *AcrPull*. Both are granted by `infra/bootstrap`, which the operator
  runs and which also creates the three app identities. CI has no right to grant or remove roles on the
  registry (a delegation limited to *AcrPull* would still have let it remove the login app's pull access or grant
  pulls to other principals), and the production configuration never touches the registry.

## Consequences

- One registry fewer to pay for and manage.
- The app identities live in the bootstrap's state: they are created before the production configuration, which
  looks them up by name.
- *AcrPush*/*AcrPull* are registry-wide: this project's deploy pipeline could push to the login app's
  repositories, and its identities can pull the login app's images. Scoping them to repositories would mean
  switching the registry to ABAC repository permissions, which changes how the login app's existing role
  assignments work, so it is not done here. A dedicated registry is the way back if that coupling matters.
- The registry's network rules are owned by the login app; pushes from GitHub-hosted runners and pulls from the
  Container Apps environment must be allowed.
- Retention for this project's images is an ACR task created by `infra/bootstrap` (weekly; deletes `photo-*`
  tags older than 30 days except the newest 10, then image data no tag refers to). Its repository filter
  matches whole names only, and `acr purge` never deletes manifests referenced by a still-tagged image index,
  so the attestations of kept images survive (verified in the acr-cli source, 2026-11-19).
