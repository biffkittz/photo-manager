# Namespace settings plus one namespace-scoped service account per tier (Temporal Cloud best practice:
# API keys bound to service accounts, one per service, scoped to the namespace it needs).
#
# API keys are intentionally NOT created here: a key created by Terraform would be stored in state. Create
# them in the Temporal Cloud UI (or tcld) for these service accounts and store them in Key Vault with
# scripts/bootstrap-keyvault.sh. Rotate at least every 90 days (docs/operations.md).
resource "temporalcloud_namespace" "this" {
  name           = var.namespace_name
  regions        = [var.region]
  api_key_auth   = true
  retention_days = var.retention_days

  namespace_lifecycle = {
    enable_delete_protection = true
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "temporalcloud_service_account" "web" {
  name        = "${var.service_account_prefix}-web"
  description = "Photo Manager API: starts, queries and cancels workflows."

  namespace_scoped_access = {
    namespace_id = temporalcloud_namespace.this.id
    permission   = "write"
  }
}

resource "temporalcloud_service_account" "worker" {
  name        = "${var.service_account_prefix}-worker"
  description = "Photo Manager Temporal workers: poll task queues and complete tasks."

  namespace_scoped_access = {
    namespace_id = temporalcloud_namespace.this.id
    permission   = "write"
  }
}
