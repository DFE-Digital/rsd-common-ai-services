variable "azure_client_id" {
  description = "Service Principal Client ID"
  type        = string
}

variable "azure_client_secret" {
  description = "Service Principal Client Secret"
  type        = string
  sensitive   = true
}

variable "azure_tenant_id" {
  description = "Service Principal Tenant ID"
  type        = string
}

variable "azure_subscription_id" {
  description = "Service Principal Subscription ID"
  type        = string
}

variable "environment" {
  description = "Environment name. Will be used along with `project_name` as a prefix for all resources."
  type        = string
}

variable "project_name" {
  description = "Project name. Will be used along with `environment` as a prefix for all resources."
  type        = string
}

variable "azure_location" {
  description = "Azure location in which to launch resources."
  type        = string
}

variable "tfvars_filename" {
  description = "tfvars filename. This file is uploaded and stored within a Storage Account, to ensure that the latest tfvars are stored in a shared place."
  type        = string
}

variable "search_service_sku" {
  description = "Search Service SKU"
  type        = string
  default     = "basic"
}

variable "search_service_replica_count" {
  description = "Search Service replica count"
  type        = number
  default     = 1
}

variable "search_service_partition_count" {
  description = "Search Service partition count"
  type        = number
  default     = 1
}

variable "search_allow_both_api_and_rbac" {
  description = "Allow the use of both API keys and RBAC authentication"
  type        = bool
  default     = true
}

variable "private_endpoint_targets" {
  description = "VNets in which to create a Search Service private endpoint."
  type = map(object({
    vnet_name                = string
    vnet_resource_group_name = string
    existing_subnet_name     = optional(string, "")
    subnet_address_prefix    = optional(string, "")
  }))
  default = {}
}

variable "tags" {
  description = "Tags to be applied to all resources"
  type        = map(string)
}

variable "vectorizer_resource_uri" {
  description = "Vectorizer resource uri."
  type        = string

  validation {
    condition     = can(regex("^https://", var.vectorizer_resource_uri))
    error_message = "vectorizer_resource_uri must use HTTPS."
  }
}

variable "vectorizer_api_key" {
  description = "Custom vectorizer API key."
  type        = string
  sensitive   = true
}

variable "vectorizer_model_name" {
  description = "vectorizer embedding model name."
  type        = string
}

variable "vectorizer_deployment_id" {
  description = "vectorizer embedding model's deployment Id."
  type        = string
}
