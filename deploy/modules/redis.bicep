// Microsoft.Cache/redis — Azure Cache for Redis: the four-layer response cache (Basic C0 in dev, Standard C1+ in prod).

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Redis SKU name: Basic | Standard | Premium.')
param skuName string = 'Basic'

@description('Redis SKU family: C (Basic/Standard) or P (Premium).')
param skuFamily string = 'C'

@description('Redis capacity: 0-6 for C-family. C0 = 250MB, C1 = 1GB, C3 = 6GB.')
param skuCapacity int = 0

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var redisName = take('redis-sd-${environment}-${suffix}', 63)

resource redis 'Microsoft.Cache/redis@2024-11-01' = {
  name: redisName
  location: location
  tags: tags
  properties: {
    sku: {
      name: skuName
      family: skuFamily
      capacity: skuCapacity
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    // Entra ID authentication — no access keys handed to consumers.
    redisConfiguration: {
      'aad-enabled': 'true'
    }
  }
}

output id string = redis.id
output name string = redis.name
// host:port the backend dials over TLS.
output host string = '${redis.properties.hostName}:${redis.properties.sslPort}'
output hostName string = redis.properties.hostName
