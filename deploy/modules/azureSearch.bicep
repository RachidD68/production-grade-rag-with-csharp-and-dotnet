// Microsoft.Search/searchServices — the managed-HA hybrid backend alternative to self-hosted Qdrant (selected when hybridBackend == 'azure-search').

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Search SKU. basic in dev, standard in prod.')
param skuName string = 'standard'

@description('Replica count (query throughput + HA).')
param replicaCount int = 1

@description('Partition count (index size + write throughput).')
param partitionCount int = 1

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var serviceName = take('srch-sd-${environment}-${suffix}', 60)

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: serviceName
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  properties: {
    replicaCount: replicaCount
    partitionCount: partitionCount
    hostingMode: 'default'
    semanticSearch: 'standard'
    // Prefer Entra ID + RBAC over admin/query keys.
    disableLocalAuth: true
    publicNetworkAccess: 'enabled'
  }
}

output id string = search.id
output name string = search.name
// The host the backend dials when running the managed search backend.
output host string = '${search.name}.search.windows.net'
output endpoint string = 'https://${search.name}.search.windows.net'
