locals {
  environment     = var.environment
  project_name    = var.project_name
  resource_prefix = "${local.environment}${local.project_name}"
  azure_location  = var.azure_location
  tags            = var.tags

  search_service_sku             = var.search_service_sku
  search_service_replica_count   = var.search_service_replica_count
  search_service_partition_count = var.search_service_partition_count
}
