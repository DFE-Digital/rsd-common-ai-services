resource "azapi_data_plane_resource" "search_index" {
  for_each = local.indexes

  type = "Microsoft.Search/searchServices/indexes@2025-09-01"

  parent_id = local.search_endpoint
  name      = each.value.name

  body = {
    fields = each.value.fields

    vectorSearch = {
      algorithms = [
        {
          name = local.vector_algorithm_name
          kind = "hnsw"

          hnswParameters = {
            m              = 4
            efConstruction = 400
            efSearch       = 500
            metric         = "cosine"
          }
        }
      ]

      profiles = [
        {
          name       = local.vector_profile_name
          algorithm  = local.vector_algorithm_name
          vectorizer = local.vectorizer_name
        }
      ]

      vectorizers = [
        {
          name = local.vectorizer_name
          kind = "customWebApi"

          customWebApiParameters = {
            uri        = var.vectorizer_api_url
            httpMethod = "POST"

            httpHeaders = {
              "api-key" = var.vectorizer_api_key
            }

            timeout = "PT60S"
          }
        }
      ]
    }

    semantic = {
      defaultConfiguration = local.semantic_configuration_name

      configurations = [
        {
          name = local.semantic_configuration_name

          prioritizedFields = {
            titleField = {
              fieldName = each.value.semantic.titleField
            }

            prioritizedContentFields = [
              for field_name in each.value.semantic.contentFields : {
                fieldName = field_name
              }
            ]
          }
        }
      ]
    }
  }

  timeouts {
    create = "10m"
    read   = "5m"
    update = "10m"
    delete = "10m"
  }
  depends_on = [
    azurerm_search_service.search
  ]
}