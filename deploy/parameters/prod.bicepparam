// deploy/parameters/prod.bicepparam — prod: App Service P2V3, Cosmos 20K RU, OpenAI (note PTU), Redis Standard 6GB.
using '../main.bicep'

param environment = 'prod'
param hybridBackend = 'qdrant'

// App Service: P2V3 (vs P0V3 in dev).
param appServicePlanSku = 'P2V3'

// Autoscale: 2–10 instances (vs 1–2 in dev). Min 2 keeps the plan always-warm for HA and
// zero-downtime slot swaps; max 10 P2V3 instances carries the ~120K queries/day
// enterprise breakpoint with headroom (Ch 25 §3.6).
param appServiceAutoscaleMin = 2
param appServiceAutoscaleMax = 10

// Cosmos: provisioned throughput, 20,000 RU/s (vs serverless in dev).
param cosmosServerless = false
param cosmosThroughput = 20000

// Azure OpenAI: S0 here for portability. NOTE for prod scale: move to a
// provisioned-throughput (PTU) commitment — set openAiSku to a provisioned SKU
// and size chat/embedding capacity to your committed PTUs in openai.bicep.
param openAiSku = 'S0'

// Redis: Standard C3 (6GB) with replica HA (vs Basic 1GB in dev).
param redisSkuName = 'Standard'
param redisSkuFamily = 'C'
param redisSkuCapacity = 3

// Storage: geo-zone-redundant for the audit archive durability requirement.
param storageSku = 'Standard_GZRS'

// Private endpoint on; vault is not publicly reachable in prod.
param keyVaultPrivateEndpoint = true
