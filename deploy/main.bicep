// deploy/main.bicep — SmartDocs Ch 25 production capstone.
//
// One VNet, three subnets (frontend/backend/data), eleven resources:
//   network, keyVault, storage, cosmos, openai, qdrant|azureSearch,
//   redis, neo4j, appInsights, appService, mcp, ingestWorker.
//
// Modules are independent; dependencies thread through outputs. Bicep derives
// the deployment order from those references — Key Vault before secret consumers,
// network first, the data/telemetry tier before the App Service that reads them.
//
// Deploy (resource-group scope):
//   az deployment group create -g rg-smartdocs-prod \
//     -f deploy/main.bicep -p deploy/parameters/prod.bicepparam

targetScope = 'resourceGroup'

@description('Logical environment name (dev, staging, prod). Threaded into every module name + tag.')
param environment string

@description('Azure region for all resources. Defaults to the resource group region.')
param location string = resourceGroup().location

@description('Hybrid retrieval backend. qdrant = self-hosted on ACI; azure-search = managed HA.')
@allowed([
  'qdrant'
  'azure-search'
])
param hybridBackend string = 'qdrant'

// --- SKU / capacity knobs, defaulted for dev and overridden per environment ---
@description('App Service Plan SKU.')
param appServicePlanSku string = 'P0V3'

@description('Cosmos in serverless mode (dev) vs provisioned throughput (prod).')
param cosmosServerless bool = true

@description('Cosmos provisioned throughput (RU/s) when not serverless.')
param cosmosThroughput int = 20000

@description('Azure OpenAI account SKU (S0 = PAYG; provisioned SKU for prod PTU).')
param openAiSku string = 'S0'

@description('Redis SKU name (Basic | Standard | Premium).')
param redisSkuName string = 'Basic'

@description('Redis SKU family (C | P).')
param redisSkuFamily string = 'C'

@description('Redis capacity (C0=0 .. C6=6).')
param redisSkuCapacity int = 0

@description('Storage account SKU.')
param storageSku string = 'Standard_LRS'

@description('Enable the Key Vault private endpoint (off in dev).')
param keyVaultPrivateEndpoint bool = false

// Built-in role definition id: Key Vault Secrets User.
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// 1. Network — must exist before any VNet-injected / private-endpoint resource.
module network 'modules/network.bicep' = {
  name: 'network'
  params: {
    environment: environment
    location: location
  }
}

// 2. Key Vault — before every secret consumer.
module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    environment: environment
    location: location
    dataSubnetId: network.outputs.dataSubnetId
    enablePrivateEndpoint: keyVaultPrivateEndpoint
  }
}

// 3. Storage — documents + immutable audit archive + backup.
module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    environment: environment
    location: location
    skuName: storageSku
  }
}

// 4. Cosmos — audit log / incident register / consent registry.
module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    environment: environment
    location: location
    serverless: cosmosServerless
    provisionedThroughput: cosmosThroughput
  }
}

// 5. Azure OpenAI — gpt-4o + text-embedding-3-small.
module openai 'modules/openai.bicep' = {
  name: 'openai'
  params: {
    environment: environment
    location: location
    skuName: openAiSku
  }
}

// 6a. Qdrant (self-hosted) — only when hybridBackend == 'qdrant'.
module qdrant 'modules/qdrant.bicep' = if (hybridBackend == 'qdrant') {
  name: 'qdrant'
  params: {
    environment: environment
    location: location
    dataSubnetId: network.outputs.dataSubnetId
  }
}

// 6b. Azure AI Search (managed HA) — the alternative when hybridBackend == 'azure-search'.
module azureSearch 'modules/azureSearch.bicep' = if (hybridBackend == 'azure-search') {
  name: 'azureSearch'
  params: {
    environment: environment
    location: location
  }
}

// 7. Redis — four-layer response cache.
module redis 'modules/redis.bicep' = {
  name: 'redis'
  params: {
    environment: environment
    location: location
    skuName: redisSkuName
    skuFamily: redisSkuFamily
    skuCapacity: redisSkuCapacity
  }
}

// 8. App Insights — Log Analytics workspace + Application Insights.
module appInsights 'modules/appInsights.bicep' = {
  name: 'appInsights'
  params: {
    environment: environment
    location: location
  }
}

// 9. Container Apps environment — shared by neo4j, mcp, ingestWorker.
module containerEnv 'modules/containerEnv.bicep' = {
  name: 'containerEnv'
  params: {
    environment: environment
    location: location
    infrastructureSubnetId: network.outputs.backendSubnetId
    logAnalyticsCustomerId: appInsights.outputs.customerId
    logAnalyticsSharedKey: listKeys(appInsights.outputs.workspaceId, '2023-09-01').primarySharedKey
  }
}

// 10. Neo4j — graph store on Container Apps.
module neo4j 'modules/neo4j.bicep' = {
  name: 'neo4j'
  params: {
    environment: environment
    location: location
    managedEnvironmentId: containerEnv.outputs.id
    keyVaultName: keyVault.outputs.name
  }
}

// Guard the conditional qdrant output: empty host when running the managed search backend.
var qdrantHost = hybridBackend == 'qdrant' ? qdrant.outputs.host : ''
var searchHost = hybridBackend == 'azure-search' ? azureSearch.outputs.host : ''

// 11. App Service — SmartDocs.Api. Explicit dependsOn per the chapter's resource graph.
module appService 'modules/appService.bicep' = {
  name: 'appService'
  params: {
    environment: environment
    location: location
    planSku: appServicePlanSku
    keyVaultName: keyVault.outputs.name
    openAiEndpoint: openai.outputs.endpoint
    cosmosEndpoint: cosmos.outputs.endpoint
    redisHost: redis.outputs.host
    qdrantHost: qdrantHost
    neo4jHost: neo4j.outputs.host
    appInsightsConnectionString: appInsights.outputs.connectionString
    blobEndpoint: storage.outputs.blobEndpoint
    hybridBackend: hybridBackend
  }
  dependsOn: [
    keyVault
    qdrant
    redis
    cosmos
    openai
    appInsights
  ]
}

// MCP server — HTTP Container App fronting the API.
module mcp 'modules/mcp.bicep' = {
  name: 'mcp'
  params: {
    environment: environment
    location: location
    managedEnvironmentId: containerEnv.outputs.id
    appInsightsConnectionString: appInsights.outputs.connectionString
    apiBaseUrl: 'https://${appService.outputs.defaultHostName}'
  }
}

// Ingest worker — scale-to-zero Container App, scales on queue depth.
module ingestWorker 'modules/ingestWorker.bicep' = {
  name: 'ingestWorker'
  params: {
    environment: environment
    location: location
    managedEnvironmentId: containerEnv.outputs.id
    appInsightsConnectionString: appInsights.outputs.connectionString
    keyVaultName: keyVault.outputs.name
  }
}

// RBAC: grant the App Service identity read access to Key Vault secrets.
resource kv 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVault.outputs.name
}

resource appKvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, appService.outputs.principalId, keyVaultSecretsUserRoleId)
  scope: kv
  properties: {
    principalId: appService.outputs.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// RBAC: Neo4j and the ingest worker also read their secrets from Key Vault.
resource neo4jKvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, neo4j.outputs.principalId, keyVaultSecretsUserRoleId)
  scope: kv
  properties: {
    principalId: neo4j.outputs.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource ingestKvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, ingestWorker.outputs.principalId, keyVaultSecretsUserRoleId)
  scope: kv
  properties: {
    principalId: ingestWorker.outputs.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// --- Outputs ---
output apiUrl string = 'https://${appService.outputs.defaultHostName}'
output mcpFqdn string = mcp.outputs.fqdn
output keyVaultUri string = keyVault.outputs.uri
output cosmosEndpoint string = cosmos.outputs.endpoint
output openAiEndpoint string = openai.outputs.endpoint
output redisHost string = redis.outputs.host
output blobEndpoint string = storage.outputs.blobEndpoint
output vectorBackend string = hybridBackend
output qdrantHost string = qdrantHost
output searchHost string = searchHost
