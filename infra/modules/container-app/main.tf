# One Container App (web or worker). Deliberately has no Container Apps "secret" blocks: the apps load their
# secrets from Key Vault with their managed identity at startup, and the custom-domain module updates the web
# app with a read-modify-write of the whole resource, which cannot carry secret values.
resource "azurerm_container_app" "this" {
  name                         = var.name
  resource_group_name          = var.resource_group_name
  container_app_environment_id = var.environment_id
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server   = var.registry_server
    identity = var.identity_id
  }

  dynamic "ingress" {
    for_each = var.ingress == null ? [] : [var.ingress]
    content {
      external_enabled           = true
      target_port                = ingress.value.target_port
      transport                  = "http"
      allow_insecure_connections = false # plain HTTP is redirected to HTTPS by the ingress

      traffic_weight {
        latest_revision = true
        percentage      = 100
      }
    }
  }

  template {
    min_replicas                     = var.min_replicas
    max_replicas                     = var.max_replicas
    termination_grace_period_seconds = var.termination_grace_period_seconds

    container {
      name   = "app"
      image  = var.image
      cpu    = var.cpu
      memory = var.memory

      dynamic "env" {
        for_each = var.env
        content {
          name  = env.key
          value = env.value
        }
      }

      startup_probe {
        transport               = "HTTP"
        port                    = var.port
        path                    = "/healthz"
        interval_seconds        = 5
        failure_count_threshold = 30
      }

      liveness_probe {
        transport               = "HTTP"
        port                    = var.port
        path                    = "/healthz"
        interval_seconds        = 30
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        port                    = var.port
        path                    = "/readyz"
        interval_seconds        = 15
        failure_count_threshold = 3
      }
    }

    dynamic "http_scale_rule" {
      for_each = var.ingress == null ? [] : [var.ingress]
      content {
        name                = "http-concurrency"
        concurrent_requests = tostring(http_scale_rule.value.concurrent_requests)
      }
    }
  }

  lifecycle {
    # CI/CD rolls out new images (az containerapp update); Terraform must not roll them back.
    ignore_changes = [template[0].container[0].image]
  }
}
