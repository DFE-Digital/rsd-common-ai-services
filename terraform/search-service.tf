resource "azurerm_search_service" "search" {
  name                = local.resource_prefix
  resource_group_name = azurerm_resource_group.default.name
  location            = azurerm_resource_group.default.location

  sku             = local.search_service_sku
  replica_count   = local.search_service_replica_count
  partition_count = local.search_service_partition_count

  local_authentication_enabled = local.search_allow_both_api_and_rbac
  authentication_failure_mode  = local.search_allow_both_api_and_rbac ? "http401WithBearerChallenge" : null

  semantic_search_sku = local.semantic_search_sku

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

