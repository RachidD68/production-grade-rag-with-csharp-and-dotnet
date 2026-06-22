// deploy/parameters/dev.bicepparam — dev: Basic SKUs, serverless Cosmos, PAYG OpenAI, Redis Basic, App Service P0V3.
using '../main.bicep'

param environment = 'dev'
param hybridBackend = 'qdrant'

// App Service: P0V3 (smallest Premium v3).
param appServicePlanSku = 'P0V3'

// Cosmos: serverless — pay per request, no minimum.
param cosmosServerless = true

// Azure OpenAI: pay-as-you-go (S0).
param openAiSku = 'S0'

// Redis: Basic C0 (250MB), single node.
param redisSkuName = 'Basic'
param redisSkuFamily = 'C'
param redisSkuCapacity = 0

// Storage: locally redundant.
param storageSku = 'Standard_LRS'

// Key Vault stays publicly reachable in dev to keep the loop fast.
param keyVaultPrivateEndpoint = false
