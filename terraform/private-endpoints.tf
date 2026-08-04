resource "azurerm_subnet" "search_private_endpoint" {
  for_each = {
    for key, target in local.private_endpoint_targets :
    key => target if target["subnet_address_prefix"] != "" && target["existing_subnet_name"] == ""
  }

  name                 = "${local.resource_prefix}-comai-search-private-endpoint"
  resource_group_name  = each.value.vnet_resource_group_name
  virtual_network_name = each.value.vnet_name

  address_prefixes = [
    each.value.subnet_address_prefix
  ]

  private_endpoint_network_policies = "Disabled"
}

resource "azurerm_private_dns_zone" "search" {
  for_each = local.private_endpoint_targets

  name                = "privatelink.search.windows.net"
  resource_group_name = each.value.vnet_resource_group_name
}

resource "azurerm_private_dns_zone_virtual_network_link" "search" {
  for_each = local.private_endpoint_targets

  name                  = "${local.resource_prefix}${each.key}comailink"
  resource_group_name   = each.value.vnet_resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.search[each.key].name
  virtual_network_id    = data.azurerm_virtual_network.private_endpoint_target[each.key].id

  registration_enabled = false
}

resource "azurerm_private_endpoint" "search" {
  for_each = local.private_endpoint_targets

  name = "${local.resource_prefix}-search.${local.private_endpoint_subnets[each.key].name}"

  location = data.azurerm_virtual_network.private_endpoint_target[each.key].location

  resource_group_name = each.value.vnet_resource_group_name
  subnet_id           = local.private_endpoint_subnets[each.key].id

  private_service_connection {
    name = "${local.resource_prefix}${each.key}"

    private_connection_resource_id = azurerm_search_service.search.id
    subresource_names              = ["searchService"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name = "${local.resource_prefix}${each.key}-search-private-link"

    private_dns_zone_ids = [azurerm_private_dns_zone.search[each.key].id]
  }

  depends_on = [
    azurerm_private_dns_zone_virtual_network_link.search
  ]
}
