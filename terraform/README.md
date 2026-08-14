This documentation covers the deployment of the infrastructure to host the app.

## Azure infrastructure

The infrastructure is managed using [Terraform](https://www.terraform.io/).<br>
The state is stored remotely in encrypted Azure storage.<br>
[Terraform workspaces](https://www.terraform.io/docs/state/workspaces.html) are used to separate environments.

#### Configuring the storage backend

The Terraform state is stored remotely in Azure, this allows multiple team members to
make changes and means the state file is backed up. The state file contains
sensitive information so access to it should be restricted, and it should be stored
encrypted at rest.

##### Create a new storage backend

This step only needs to be done once per project (eg. not per environment).
If it has already been created, obtain the storage backend attributes and skip to the next step.

The [Azure tutorial](https://docs.microsoft.com/en-us/azure/developer/terraform/store-state-in-azure-storage) outlines the steps to create a storage account and container for the state file. You will need:

- resource_group_name: The name of the resource group used for the Azure Storage account.
- storage_account_name: The name of the Azure Storage account.
- container_name: The name of the blob container.
- key: The name of the state store file to be created.

##### Create a backend configuration file

Create a new file named `backend.vars` with the following content:

```
resource_group_name  = [the name of the Azure resource group]
storage_account_name = [the name of the Azure Storage account]
container_name       = [the name of the blob container]
key                  = "terraform.tstate"
```

##### Install dependencies

We can use [Homebrew](https://brew.sh) to install the dependecies we need to deploy the infrastructure (eg. tfenv, Azure cli).
These are listed in the `Brewfile`

to install, run:

```
$ brew bundle
```

##### Log into azure with the Azure CLI

Log in to your account:

```
$ az login
```

Confirm which account you are currently using:

```
$ az account show
```

To list the available subscriptions, run:

```
$ az account list
```

Then if needed, switch to it using the 'id':

```
$ az account set --subscription <id>
```

##### Initialise Terraform

Install the required terraform version with the Terraform version manager `tfenv`:

```
$ tfenv install
```

Initialize Terraform to download the required Terraform modules and configure the remote state backend
to use the settings you specified in the previous step.

`$ terraform init -backend-config=backend.vars`

##### Create a Terraform variables file

Each environment will need it's own `tfvars` file.

Copy the `terraform.tfvars.example` to `environment-name.tfvars` and modify the contents as required

##### Create the infrastructure

Now Terraform has been initialised you can create a workspace if needed:

`$ terraform workspace new staging`

Or to check what workspaces already exist:

`$ terraform workspace list`

Switch to the new or existing workspace:

`$ terraform workspace select staging`

Plan the changes:

`$ terraform plan -var-file=staging.tfvars`

Terraform will ask you to provide any variables not specified in an `*.auto.tfvars` file.
Now you can run:

`$ terraform apply -var-file=staging.tfvars`

If everything looks good, answer `yes` and wait for the new infrastructure to be created.

##### Azure resources

<!-- BEGIN_TF_DOCS -->
## Requirements

| Name | Version |
| ---- | ------- |
| <a name="requirement_terraform"></a> [terraform](#requirement\_terraform) | ~> 1.15 |
| <a name="requirement_azurerm"></a> [azurerm](#requirement\_azurerm) | ~> 4.0 |
| <a name="requirement_null"></a> [null](#requirement\_null) | ~> 3.2 |

## Providers

| Name | Version |
| ---- | ------- |
| <a name="provider_azurerm"></a> [azurerm](#provider\_azurerm) | 4.81.0 |
| <a name="provider_null"></a> [null](#provider\_null) | 3.3.0 |

## Modules

No modules.

## Resources

| Name | Type |
| ---- | ---- |
| [azurerm_private_dns_zone.search](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_dns_zone) | resource |
| [azurerm_private_dns_zone_virtual_network_link.search](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_dns_zone_virtual_network_link) | resource |
| [azurerm_private_endpoint.search](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_endpoint) | resource |
| [azurerm_resource_group.default](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/resource_group) | resource |
| [azurerm_search_service.search](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/search_service) | resource |
| [azurerm_storage_account.tfvars](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_account) | resource |
| [azurerm_storage_account_network_rules.tfvars](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_account_network_rules) | resource |
| [azurerm_storage_blob.tfvars](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_blob) | resource |
| [azurerm_storage_container.tfvars](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_container) | resource |
| [azurerm_subnet.search_private_endpoint](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet) | resource |
| [null_resource.tfvars](https://registry.terraform.io/providers/hashicorp/null/latest/docs/resources/resource) | resource |
| [azurerm_subnet.private_endpoint](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/data-sources/subnet) | data source |
| [azurerm_virtual_network.private_endpoint_target](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/data-sources/virtual_network) | data source |

## Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_azure_client_id"></a> [azure\_client\_id](#input\_azure\_client\_id) | Service Principal Client ID | `string` | n/a | yes |
| <a name="input_azure_client_secret"></a> [azure\_client\_secret](#input\_azure\_client\_secret) | Service Principal Client Secret | `string` | n/a | yes |
| <a name="input_azure_location"></a> [azure\_location](#input\_azure\_location) | Azure location in which to launch resources. | `string` | n/a | yes |
| <a name="input_azure_subscription_id"></a> [azure\_subscription\_id](#input\_azure\_subscription\_id) | Service Principal Subscription ID | `string` | n/a | yes |
| <a name="input_azure_tenant_id"></a> [azure\_tenant\_id](#input\_azure\_tenant\_id) | Service Principal Tenant ID | `string` | n/a | yes |
| <a name="input_environment"></a> [environment](#input\_environment) | Environment name. Will be used along with `project_name` as a prefix for all resources. | `string` | n/a | yes |
| <a name="input_private_endpoint_targets"></a> [private\_endpoint\_targets](#input\_private\_endpoint\_targets) | VNets in which to create a Search Service private endpoint. | <pre>map(object({<br/>    vnet_name                = string<br/>    vnet_resource_group_name = string<br/>    existing_subnet_name     = optional(string, "")<br/>    subnet_address_prefix    = optional(string, "")<br/>  }))</pre> | `{}` | no |
| <a name="input_project_name"></a> [project\_name](#input\_project\_name) | Project name. Will be used along with `environment` as a prefix for all resources. | `string` | n/a | yes |
| <a name="input_search_allow_both_api_and_rbac"></a> [search\_allow\_both\_api\_and\_rbac](#input\_search\_allow\_both\_api\_and\_rbac) | Allow the use of both API keys and RBAC authentication | `bool` | `true` | no |
| <a name="input_search_service_partition_count"></a> [search\_service\_partition\_count](#input\_search\_service\_partition\_count) | Search Service partition count | `number` | `1` | no |
| <a name="input_search_service_replica_count"></a> [search\_service\_replica\_count](#input\_search\_service\_replica\_count) | Search Service replica count | `number` | `1` | no |
| <a name="input_search_service_sku"></a> [search\_service\_sku](#input\_search\_service\_sku) | Search Service SKU | `string` | `"basic"` | no |
| <a name="input_tags"></a> [tags](#input\_tags) | Tags to be applied to all resources | `map(string)` | n/a | yes |
| <a name="input_tfvars_filename"></a> [tfvars\_filename](#input\_tfvars\_filename) | tfvars filename. This file is uploaded and stored within a Storage Account, to ensure that the latest tfvars are stored in a shared place. | `string` | n/a | yes |

## Outputs

No outputs.
<!-- END_TF_DOCS -->
