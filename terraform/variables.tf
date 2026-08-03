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

variable "tags" {
  description = "Tags to be applied to all resources"
  type        = map(string)
}
