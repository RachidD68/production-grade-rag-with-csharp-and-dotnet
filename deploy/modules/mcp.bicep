// Microsoft.App/containerApps — SmartDocs.Mcp (HTTP MCP server) on Container Apps with external ingress and a system-assigned identity.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Container Apps managed environment id.')
param managedEnvironmentId string

@description('Container image for SmartDocs.Mcp.')
param image string = 'ghcr.io/rachiddahir/smartdocs-mcp:latest'

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('SmartDocs.Api base URL the MCP tools call.')
param apiBaseUrl string = ''

@description('Min replicas (keep at least 1 so the MCP endpoint is always warm).')
param minReplicas int = 1

@description('Max replicas.')
param maxReplicas int = 3

@description('Canary weight (%) sent to the newest ("next") revision. Drive 0 -> 10 -> 50 -> 100 across the rollout; the pinned previous ("current") revision takes the remainder.')
@minValue(0)
@maxValue(100)
param canaryWeight int = 100

@description('Revision name of the pinned previous ("current") revision that absorbs non-canary traffic. Empty on the first deploy (100% necessarily goes to latest).')
param previousRevisionName string = ''

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var appName = 'ca-mcp-${environment}-${suffix}'

// --- Progressive delivery / blue-green traffic split (Ch 25 §3.4) ----------------------
// Revision mode is 'Multiple' so two revisions can serve at once. The newest revision is
// labelled 'next' and receives `canaryWeight`%; a pinned previous revision labelled
// 'current' absorbs the remainder. The previous-revision entry only joins the array once
// `previousRevisionName` is supplied (you cannot weight a revision that does not exist),
// so the very first deploy sends 100% to latest.
//
// Canary / rollback flow:
//   deploy at canaryWeight=0  -> smoke + faithfulness probe on the 'next' revision
//   -> shift weights up on green (0 -> 10 -> 50 -> 100)
//   -> auto-rollback by setting canaryWeight back to 0 (traffic snaps to 'current')
//      if the rolling faithfulness signal drops. See docs/runbooks/canary-rollback.md.
var nextTraffic = [
  {
    latestRevision: true
    weight: canaryWeight
    label: 'next'
  }
]
var currentTraffic = empty(previousRevisionName) ? [] : [
  {
    revisionName: previousRevisionName
    weight: 100 - canaryWeight
    label: 'current'
  }
]
var trafficSplit = concat(nextTraffic, currentTraffic)

resource mcp 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: managedEnvironmentId
    configuration: {
      // Multiple-revision mode is required to weight traffic across two live revisions.
      activeRevisionsMode: 'Multiple'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        // Weighted canary split — see the trafficSplit var above for the rollout flow.
        traffic: trafficSplit
      }
    }
    template: {
      containers: [
        {
          name: 'smartdocs-mcp'
          image: image
          env: [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
            {
              name: 'SmartDocs__Api__BaseUrl'
              value: apiBaseUrl
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output id string = mcp.id
output name string = mcp.name
output fqdn string = mcp.properties.configuration.ingress.fqdn
output principalId string = mcp.identity.principalId
