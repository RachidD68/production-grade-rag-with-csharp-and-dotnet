// SmartDocs workload — provisioned inside the resource group from main.bicep.

param location string
param environment string
param aoaiEndpoint string
@secure()
param aoaiApiKey string
param tags object

var prefix = 'smartdocs-${environment}'

resource search 'Microsoft.Search/searchServices@2024-03-01-preview' = {
  name: '${prefix}-search'
  location: location
  tags: tags
  sku: { name: 'standard' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    semanticSearch: 'standard'
  }
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-08-15' = {
  name: '${prefix}-cosmos'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [ { locationName: location, failoverPriority: 0 } ]
    capabilities: [ { name: 'EnableServerless' } ]
  }
}

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: '${prefix}-redis'
  location: location
  tags: tags
  properties: {
    sku: { name: 'Basic', family: 'C', capacity: 0 }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-appi'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-cae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-api'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'http' }
      secrets: [
        { name: 'aoai-key', value: aoaiApiKey }
      ]
    }
    template: {
      containers: [
        {
          name: 'smartdocs-api'
          image: 'ghcr.io/rachiddahir/smartdocs-api:latest'
          env: [
            { name: 'SmartDocs__Llm__Provider', value: 'AzureOpenAI' }
            { name: 'SmartDocs__Llm__Endpoint', value: aoaiEndpoint }
            { name: 'SmartDocs__Llm__ApiKey', secretRef: 'aoai-key' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output searchEndpoint string = 'https://${search.name}.search.windows.net'
