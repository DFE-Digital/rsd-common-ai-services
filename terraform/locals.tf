locals {
  environment     = var.environment
  project_name    = var.project_name
  resource_prefix = "${local.environment}${local.project_name}"
  azure_location  = var.azure_location
  tfvars_filename = var.tfvars_filename
  tags            = var.tags

  search_service_sku             = var.search_service_sku
  search_service_replica_count   = var.search_service_replica_count
  search_service_partition_count = var.search_service_partition_count
  search_allow_both_api_and_rbac = var.search_allow_both_api_and_rbac

  private_endpoint_targets = var.private_endpoint_targets

  private_endpoint_subnets = merge({
    for key, target in local.private_endpoint_targets : key => data.azurerm_subnet.private_endpoint[key] if target["existing_subnet_name"] != "" && target["subnet_address_prefix"] == ""
    },
    {
      for key, target in local.private_endpoint_targets : key => azurerm_subnet.search_private_endpoint[key] if target["existing_subnet_name"] == "" && target["subnet_address_prefix"] != ""
  })

  is_windows = can(regex("^[A-Za-z]:", abspath(path.root)))
  bash       = local.is_windows ? "C:/Program Files/Git/bin/bash.exe" : "/bin/bash"

  index_files = fileset("${path.module}/indexes", "*.json.tftpl")
  indexes = {
    for file_name in local.index_files :
    trimsuffix(file_name, ".json.tftpl") => jsondecode(templatefile("${path.module}/indexes/${file_name}",
      {
        vector_profile_name = local.vector_profile_name
      }
      )
    )
  }
  search_endpoint             = "${azurerm_search_service.search.name}.search.windows.net"
  vectorizer_name             = "rsd-vectorizer"
  vector_algorithm_name       = "rsd-vector-config"
  vector_profile_name         = "rsd-vector-profile"
  semantic_configuration_name = "rsd-semantic-config"
}
