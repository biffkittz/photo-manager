# photo.biffkittz.com -> web Container App with a free, auto-renewed managed certificate.
#
# Sequence (Azure requires it): DNS records -> hostname added to the app (binding disabled) -> managed
# certificate issued (CNAME validation) -> hostname bound to the certificate (SNI).
#
# azapi is used for the final binding only: azurerm 5.14 can create the managed certificate but its
# azurerm_container_app_custom_domain resource can bind only uploaded certificates. azapi_update_resource
# reads the app, merges the customDomains change and writes it back; azurerm_container_app round-trips that
# binding (its ingress.custom_domain is computed), so the two providers do not fight.
#
# The record is DNS-only (not proxied through Cloudflare) so certificate validation and renewal can reach
# Azure directly.
data "cloudflare_zone" "this" {
  filter = {
    name = var.cloudflare_zone_name
  }
}

resource "cloudflare_dns_record" "cname" {
  zone_id = data.cloudflare_zone.this.id
  name    = var.hostname
  type    = "CNAME"
  content = var.container_app_fqdn
  ttl     = 300
  proxied = false
  comment = "Photo Manager web app (managed by Terraform)"
}

resource "cloudflare_dns_record" "verification" {
  zone_id = data.cloudflare_zone.this.id
  name    = "asuid.${var.hostname}"
  type    = "TXT"
  content = var.verification_id
  ttl     = 300
  comment = "Azure Container Apps domain verification (managed by Terraform)"
}

resource "azurerm_container_app_custom_domain" "this" {
  name             = var.hostname
  container_app_id = var.container_app_id

  depends_on = [cloudflare_dns_record.cname, cloudflare_dns_record.verification]

  lifecycle {
    # The managed certificate is bound asynchronously below; ignoring these stops Terraform from
    # replacing the domain on every plan.
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }
}

resource "azurerm_container_app_environment_managed_certificate" "this" {
  name                         = replace(var.hostname, ".", "-")
  container_app_environment_id = var.environment_id
  subject_name                 = var.hostname
  domain_control_validation    = "CNAME"

  depends_on = [azurerm_container_app_custom_domain.this]
}

resource "azapi_update_resource" "bind_certificate" {
  type        = "Microsoft.App/containerApps@2025-07-01"
  resource_id = var.container_app_id

  body = {
    properties = {
      configuration = {
        ingress = {
          customDomains = [
            {
              name          = var.hostname
              bindingType   = "SniEnabled"
              certificateId = azurerm_container_app_environment_managed_certificate.this.id
            },
          ]
        }
      }
    }
  }
}
