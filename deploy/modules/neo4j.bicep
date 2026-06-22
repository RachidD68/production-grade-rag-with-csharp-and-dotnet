// Microsoft.App/containerApps — Neo4j (neo4j:5.26-community) on Container Apps: the graph store for the GraphRAG retrieval path.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Container Apps managed environment id.')
param managedEnvironmentId string

@description('Neo4j container image.')
param image string = 'neo4j:5.26-community'

@description('Key Vault name holding the neo4j-password secret.')
param keyVaultName string

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var appName = 'ca-neo4j-${environment}-${suffix}'

// System-assigned identity reads the bootstrap password from Key Vault.
resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource neo4j 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: managedEnvironmentId
    configuration: {
      // Internal-only ingress on the bolt port — backend talks to it, nothing public.
      ingress: {
        external: false
        targetPort: 7687
        transport: 'tcp'
        exposedPort: 7687
      }
      secrets: [
        {
          name: 'neo4j-auth'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/neo4j-auth'
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'neo4j'
          image: image
          env: [
            {
              name: 'NEO4J_AUTH'
              secretRef: 'neo4j-auth'
            }
            {
              name: 'NEO4J_server_memory_pagecache_size'
              value: '1G'
            }
          ]
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output id string = neo4j.id
output name string = neo4j.name
// bolt host the backend dials inside the environment.
output host string = 'bolt://${neo4j.properties.configuration.ingress.fqdn}:7687'
output principalId string = neo4j.identity.principalId
