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

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var appName = 'ca-mcp-${environment}-${suffix}'

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
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
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
