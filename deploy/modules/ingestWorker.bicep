// Microsoft.App/containerApps — the IngestEventConsumer worker on Container Apps: scale-to-zero, scales out on Service Bus queue depth.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Container Apps managed environment id.')
param managedEnvironmentId string

@description('Container image for the ingest worker.')
param image string = 'ghcr.io/rachiddahir/smartdocs-ingest:latest'

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Key Vault name backing the Service Bus connection secret reference.')
param keyVaultName string

@description('Service Bus queue the worker drains.')
param ingestQueueName string = 'ingest'

@description('Max replicas under load.')
param maxReplicas int = 10

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var appName = 'ca-ingest-${environment}-${suffix}'

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource ingest 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: managedEnvironmentId
    configuration: {
      // No ingress — this is a background worker, not a service.
      activeRevisionsMode: 'Single'
      secrets: [
        {
          name: 'servicebus-connection'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/servicebus-connection'
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'smartdocs-ingest'
          image: image
          env: [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
            {
              name: 'SmartDocs__Ingestion__Queue'
              value: ingestQueueName
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      // --- KEDA autoscale (Ch 25 §3.6) --------------------------------------------------
      // Ingestion is bursty and queue-driven, so the worker scales on Service Bus queue
      // depth and goes scale-to-zero between batches (minReplicas 0 = no idle cost). KEDA
      // targets ~20 unprocessed messages per replica and adds replicas up to maxReplicas
      // as the backlog grows — a 120K-doc enterprise re-index drains across the full fan-out
      // then collapses back to zero, instead of pinning a fixed pool.
      scale: {
        // Scale to zero when idle; KEDA Service Bus rule scales out on queue depth.
        minReplicas: 0
        maxReplicas: maxReplicas
        rules: [
          // Service Bus queue-depth rule: +1 replica per ~20 backlogged messages.
          {
            name: 'queue-depth'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                queueName: ingestQueueName
                messageCount: '20'
              }
              auth: [
                {
                  secretRef: 'servicebus-connection'
                  triggerParameter: 'connection'
                }
              ]
            }
          }
        ]
      }
    }
  }
}

output id string = ingest.id
output name string = ingest.name
output principalId string = ingest.identity.principalId
