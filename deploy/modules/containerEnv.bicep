// Microsoft.App/managedEnvironments — the shared Container Apps environment hosting Neo4j, the MCP server, and the ingest worker; joined to the backend subnet and Log Analytics.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Backend subnet id — the Container Apps environment is VNet-injected here.')
param infrastructureSubnetId string

@description('Log Analytics workspace customer id (GUID).')
param logAnalyticsCustomerId string

@description('Log Analytics workspace shared key.')
@secure()
param logAnalyticsSharedKey string

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var envName = 'cae-smartdocs-${environment}-${suffix}'

resource managedEnv 'Microsoft.App/managedEnvironments@2024-10-02-preview' = {
  name: envName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnetId
      internal: true
    }
    zoneRedundant: false
  }
}

output id string = managedEnv.id
output name string = managedEnv.name
output defaultDomain string = managedEnv.properties.defaultDomain
