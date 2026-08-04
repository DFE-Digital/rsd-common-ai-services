data "azurerm_virtual_network" "private_endpoint_target" {
  for_each = local.private_endpoint_targets

  name                = each.value.vnet_name
  resource_group_name = each.value.vnet_resource_group_name
}

data "azurerm_subnet" "private_endpoint" {
  for_each = {
    for key, target in local.private_endpoint_targets :
    key => target if target["existing_subnet_name"] != ""
  }

  name                 = each.value.existing_subnet_name
  virtual_network_name = each.value.vnet_name
  resource_group_name  = each.value.vnet_resource_group_name
}
