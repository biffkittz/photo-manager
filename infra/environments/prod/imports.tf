# The photo.joqqs namespace already exists in Temporal Cloud: adopt it instead of creating a new one.
# Set temporal_import_existing_namespace = false to create it from scratch.
import {
  for_each = var.manage_temporal_cloud && var.temporal_import_existing_namespace ? toset([local.temporal_namespace]) : toset([])
  to       = module.temporal_cloud[0].temporalcloud_namespace.this
  id       = each.value
}
