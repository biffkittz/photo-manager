output "namespace_id" {
  description = "Namespace id, e.g. photo.joqqs."
  value       = temporalcloud_namespace.this.id
}

output "grpc_endpoint" {
  description = "gRPC endpoint for API-key clients."
  value       = temporalcloud_namespace.this.endpoints.grpc_address
}

output "service_account_ids" {
  description = "Service accounts that need API keys (create in the Temporal Cloud UI)."
  value = {
    web    = temporalcloud_service_account.web.id
    worker = temporalcloud_service_account.worker.id
  }
}
