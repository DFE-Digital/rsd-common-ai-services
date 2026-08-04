locals {
  environment     = var.environment
  project_name    = var.project_name
  resource_prefix = "${local.environment}${local.project_name}"
  azure_location  = var.azure_location
  tags            = var.tags

  search_service_sku             = var.search_service_sku
  search_service_replica_count   = var.search_service_replica_count
  search_service_partition_count = var.search_service_partition_count

  private_endpoint_targets = var.private_endpoint_targets

  private_endpoint_subnets = merge({
    for key, target in local.private_endpoint_targets : key => data.azurerm_subnet.private_endpoint[key] if target["existing_subnet_name"] != "" && target["subnet_address_prefix"] == ""
    },
    {
      for key, target in local.private_endpoint_targets : key => azurerm_subnet.search_private_endpoint[key] if target["existing_subnet_name"] == "" && target["subnet_address_prefix"] != ""
  })
}
