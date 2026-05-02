// SmartDocs Azure deployment — Ch 25 capstone.
//
// Provisions:
//   - Azure AI Search (Standard tier; vector + hybrid)
//   - Azure Cosmos DB for NoSQL (document store)
//   - Azure Cache for Redis (Basic tier)
//   - Azure Container Apps environment + the SmartDocs.Api container app
//   - Azure Monitor (Log Analytics workspace + Application Insights)
//
// Deploy:
//   az deployment sub create \
//     --name smartdocs-prod \
//     --location canadacentral \
//     --template-file main.bicep \
//     --parameters environment=prod aoaiEndpoint=$AOAI_ENDPOINT aoaiApiKey=$AOAI_KEY

targetScope = 'subscription'

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string = 'dev'

@description('Azure region for all resources.')
param location string = 'canadacentral'

@description('Azure OpenAI endpoint URL.')
param aoaiEndpoint string

@description('Azure OpenAI API key.')
@secure()
param aoaiApiKey string

@description('Tag set applied to every resource for cost tracking.')
param tags object = {
  product: 'SmartDocs'
  environment: environment
  managedBy: 'bicep'
}

var rgName = 'rg-smartdocs-${environment}'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: rgName
  location: location
  tags: tags
}

module workload 'workload.bicep' = {
  name: 'smartdocs-workload'
  scope: resourceGroup
  params: {
    location: location
    environment: environment
    aoaiEndpoint: aoaiEndpoint
    aoaiApiKey: aoaiApiKey
    tags: tags
  }
}

output apiUrl string = workload.outputs.apiUrl
output searchEndpoint string = workload.outputs.searchEndpoint
