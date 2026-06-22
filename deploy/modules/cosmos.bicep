// Microsoft.DocumentDB/databaseAccounts — Cosmos DB for NoSQL: the queryable audit log, incident register, and consent registry.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Serverless capacity in dev; provisioned throughput in prod.')
param serverless bool = true

@description('Provisioned throughput (RU/s) when serverless is false.')
param provisionedThroughput int = 20000

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var accountName = take('cosmos-sd-${environment}-${suffix}', 44)
var databaseName = 'smartdocs'

// Serverless is enabled via a capability; provisioned mode omits it.
var capabilities = serverless ? [ { name: 'EnableServerless' } ] : []

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: capabilities
    // Prefer Entra ID / RBAC over account keys.
    disableLocalAuth: true
    minimalTlsVersion: 'Tls12'
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmos
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
    // Provisioned throughput is set at the database level when not serverless.
    options: serverless ? {} : {
      throughput: provisionedThroughput
    }
  }
}

// One container per compliance concern; partitioned by tenant.
var containerNames = [
  'audit-log'
  'incident-register'
  'consent-registry'
]

resource containers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for name in containerNames: {
    parent: database
    name: name
    properties: {
      resource: {
        id: name
        partitionKey: {
          paths: [ '/tenantId' ]
          kind: 'Hash'
        }
      }
    }
  }
]

output id string = cosmos.id
output name string = cosmos.name
output endpoint string = cosmos.properties.documentEndpoint
