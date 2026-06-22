// Microsoft.Web/serverfarms + sites — Linux App Service hosting SmartDocs.Api: system-assigned identity, Key Vault references for secrets, /health/ready probe.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('App Service Plan SKU. P0V3 in dev, P2V3 in prod.')
param planSku string = 'P0V3'

@description('Container image for SmartDocs.Api.')
param image string = 'ghcr.io/rachiddahir/smartdocs-api:latest'

@description('Key Vault name backing the @Microsoft.KeyVault app-setting references.')
param keyVaultName string

@description('Azure OpenAI endpoint.')
param openAiEndpoint string

@description('Cosmos DB endpoint.')
param cosmosEndpoint string

@description('Redis host (host:port).')
param redisHost string

@description('Qdrant host (private IP / gRPC host). Empty when running the managed search backend.')
param qdrantHost string = ''

@description('Neo4j bolt host.')
param neo4jHost string = ''

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Storage blob endpoint.')
param blobEndpoint string = ''

@description('Hybrid retrieval backend: qdrant | azure-search.')
param hybridBackend string = 'qdrant'

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var planName = 'plan-smartdocs-${environment}-${suffix}'
var siteName = 'app-smartdocs-${environment}-${suffix}'

// Key Vault reference strings — the .NET config provider resolves these at runtime
// via the site's managed identity (granted Key Vault Secrets User in main.bicep).
var openAiKeyRef = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=openai-api-key)'
var neo4jPasswordRef = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=neo4j-password)'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  sku: {
    name: planSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: siteName
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOCKER|${image}'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/health/ready'
      appSettings: [
        {
          name: 'WEBSITES_PORT'
          value: '8080'
        }
        // --- Telemetry ---
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        // --- LLM (Azure OpenAI) ---
        {
          name: 'SmartDocs__Llm__Provider'
          value: 'AzureOpenAI'
        }
        {
          name: 'SmartDocs__Llm__Endpoint'
          value: openAiEndpoint
        }
        {
          name: 'SmartDocs__Llm__ChatModel'
          value: 'gpt-4o'
        }
        {
          name: 'SmartDocs__Llm__EmbeddingModel'
          value: 'text-embedding-3-small'
        }
        {
          name: 'SmartDocs__Llm__ApiKey'
          value: openAiKeyRef
        }
        // --- Retrieval backend (switched by hybridBackend) ---
        {
          name: 'SmartDocs__Retrieval__Backend'
          value: hybridBackend
        }
        {
          name: 'SmartDocs__Retrieval__Qdrant__Host'
          value: qdrantHost
        }
        {
          name: 'SmartDocs__Graph__Neo4j__Uri'
          value: neo4jHost
        }
        {
          name: 'SmartDocs__Graph__Neo4j__Password'
          value: neo4jPasswordRef
        }
        // --- Cache ---
        {
          name: 'SmartDocs__Cache__Redis__Host'
          value: redisHost
        }
        // --- Compliance store ---
        {
          name: 'SmartDocs__Audit__Cosmos__Endpoint'
          value: cosmosEndpoint
        }
        {
          name: 'SmartDocs__Storage__BlobEndpoint'
          value: blobEndpoint
        }
      ]
    }
  }
}

output id string = site.id
output name string = site.name
output defaultHostName string = site.properties.defaultHostName
// System-assigned identity — granted Key Vault Secrets User + data-plane roles in main.
output principalId string = site.identity.principalId
