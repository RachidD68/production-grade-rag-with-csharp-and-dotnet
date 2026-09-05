// Microsoft.Web/serverfarms + sites — Linux App Service hosting SmartDocs.Api: system-assigned identity, Key Vault references for secrets, /health/ready probe.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('App Service Plan SKU. P0V3 in dev, P2V3 in prod.')
param planSku string = 'P0V3'

// --- Autoscale bounds (Ch 25 §3.6, capacity breakpoints) -------------------------------
// The chapter sizes the API against three breakpoints: ~1,200 queries/day (small),
// ~12K/day (mid), ~120K/day (enterprise). Horizontal scale-out makes "scale past the
// breakpoint" executable: dev rides 1–2 instances, prod 2–10. The prod maximum of 10
// P2V3 instances is what carries the ~120K/day enterprise breakpoint with headroom; lift
// `autoscaleMaxInstances` (and/or the SKU) to go beyond it.
@description('Minimum App Service Plan instances. dev 1, prod 2 (always-warm for HA + zero-downtime slot swaps).')
@minValue(1)
param autoscaleMinInstances int = 1

@description('Maximum App Service Plan instances scale-out can reach. prod 10 P2V3 instances carries the ~120K queries/day enterprise breakpoint.')
@minValue(1)
param autoscaleMaxInstances int = 2

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

@description('Azure App Configuration endpoint. The app reads feature flags at request scope via its managed identity (granted App Configuration Data Reader in main.bicep).')
param appConfigEndpoint string = ''

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

// Shared app settings — the production site and the blue-green 'staging' slot run the
// SAME configuration so a slot swap is a true like-for-like cutover (Ch 25 §3.4).
var appSettings = [
  {
    name: 'WEBSITES_PORT'
    value: '8080'
  }
  // --- Telemetry ---
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsightsConnectionString
  }
  // --- Config + feature management (Azure App Configuration) ---
  // The .NET App Configuration provider connects with the site's managed identity
  // (App Configuration Data Reader) and refreshes feature flags at request scope.
  {
    name: 'SmartDocs__AppConfig__Endpoint'
    value: appConfigEndpoint
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
    value: 'gpt-5.6-terra'
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

// --- Autoscale (Ch 25 §3.6) -----------------------------------------------------------
// Scale the plan horizontally on sustained load. Scale OUT when CPU > 70% (compute-bound
// retrieval/generation) OR the HTTP queue backs up (requests waiting on a worker); scale
// IN when CPU falls below 30%, one instance at a time on a longer cooldown so we shed
// capacity gently. Bounds come from `autoscaleMin/MaxInstances` — prod's max of 10 is what
// makes the ~120K queries/day enterprise breakpoint reachable without a manual resize.
resource planAutoscale 'Microsoft.Insights/autoscaleSettings@2022-10-01' = {
  name: 'autoscale-${planName}'
  location: location
  tags: tags
  properties: {
    enabled: true
    targetResourceUri: plan.id
    profiles: [
      {
        name: 'cpu-and-http-queue'
        capacity: {
          minimum: string(autoscaleMinInstances)
          maximum: string(autoscaleMaxInstances)
          default: string(autoscaleMinInstances)
        }
        rules: [
          // Scale OUT: average CPU > 70% over 5 min → +1 instance (5 min cooldown).
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: plan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 70
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          // Scale OUT: HTTP queue length > 100 over 5 min → +1 instance (requests are
          // waiting on a free worker, which CPU alone can miss on I/O-bound waits).
          {
            metricTrigger: {
              metricName: 'HttpQueueLength'
              metricResourceUri: plan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 100
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          // Scale IN: average CPU < 30% over 10 min → -1 instance (10 min cooldown).
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: plan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT10M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: 30
            }
            scaleAction: {
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT10M'
            }
          }
        ]
      }
    ]
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
      appSettings: appSettings
    }
  }
}

// --- Blue-green deployment slot (Ch 25 §3.4) -------------------------------------------
// Canary / rollback flow for the App Service API:
//   1. Deploy the new image to the 'staging' slot at 0% production traffic.
//   2. Run smoke tests + a faithfulness probe against the slot's own hostname.
//   3. Swap 'staging' <-> 'production' — the warmed slot takes 100% atomically.
//   4. Auto-rollback: if the rolling faithfulness signal drops post-swap, swap back
//      (the previous image is still warm in the now-'staging' slot). See
//      docs/runbooks/canary-rollback.md.
// The slot inherits the SAME app settings as production (shared `appSettings` var) so the
// swap is a true like-for-like cutover, and carries its own managed identity for RBAC.
resource stagingSlot 'Microsoft.Web/sites/slots@2024-04-01' = {
  parent: site
  name: 'staging'
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
      appSettings: appSettings
    }
  }
}

output id string = site.id
output name string = site.name
output defaultHostName string = site.properties.defaultHostName
// System-assigned identity — granted Key Vault Secrets User + data-plane roles in main.
output principalId string = site.identity.principalId
// Blue-green staging slot — its own identity also needs the data-plane roles granted.
output slotName string = stagingSlot.name
output slotPrincipalId string = stagingSlot.identity.principalId
