// deploy/parameters/staging.bicepparam — staging: mid SKUs between dev and prod.
using '../main.bicep'

param environment = 'staging'
param hybridBackend = 'qdrant'

// App Service: P1V3 — one step up from dev.
param appServicePlanSku = 'P1V3'

// Cosmos: provisioned but modest throughput.
param cosmosServerless = false
param cosmosThroughput = 4000

// Azure OpenAI: still PAYG for staging.
param openAiSku = 'S0'

// Redis: Standard C1 (1GB) with the replica HA of the Standard tier.
param redisSkuName = 'Standard'
param redisSkuFamily = 'C'
param redisSkuCapacity = 1

// Storage: zone-redundant.
param storageSku = 'Standard_ZRS'

// Private endpoint on, matching prod's network posture.
param keyVaultPrivateEndpoint = true
