// Microsoft.ContainerInstance/containerGroups — Qdrant (qdrant/qdrant) on ACI with a persistent Azure file share and a stable private IP in the data subnet.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Data subnet id — the container group gets a stable private IP here.')
param dataSubnetId string

@description('Qdrant container image tag.')
param image string = 'qdrant/qdrant:v1.12.5'

@description('vCPU for the Qdrant container.')
param cpuCores int = 2

@description('Memory (GiB) for the Qdrant container.')
param memoryGb int = 4

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var groupName = 'aci-qdrant-${environment}-${suffix}'
// Backing storage account for the persistent file share (snapshots/collections).
var shareStorageName = take('stqdrant${environment}${suffix}', 24)
var shareName = 'qdrant-storage'

resource shareStorage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: shareStorageName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2024-01-01' = {
  parent: shareStorage
  name: 'default'
}

resource qdrantShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2024-01-01' = {
  parent: fileService
  name: shareName
  properties: {
    shareQuota: 100
  }
}

resource qdrant 'Microsoft.ContainerInstance/containerGroups@2023-05-01' = {
  name: groupName
  location: location
  tags: tags
  properties: {
    osType: 'Linux'
    restartPolicy: 'Always'
    // Private IP in the data subnet — stable address for the backend to reach.
    ipAddress: {
      type: 'Private'
      ports: [
        { protocol: 'TCP', port: 6333 }
        { protocol: 'TCP', port: 6334 }
      ]
    }
    subnetIds: [
      {
        id: dataSubnetId
      }
    ]
    containers: [
      {
        name: 'qdrant'
        properties: {
          image: image
          ports: [
            { protocol: 'TCP', port: 6333 }
            { protocol: 'TCP', port: 6334 }
          ]
          resources: {
            requests: {
              cpu: cpuCores
              memoryInGB: memoryGb
            }
          }
          volumeMounts: [
            {
              name: 'qdrant-storage'
              mountPath: '/qdrant/storage'
            }
          ]
        }
      }
    ]
    volumes: [
      {
        name: 'qdrant-storage'
        azureFile: {
          shareName: shareName
          storageAccountName: shareStorage.name
          storageAccountKey: shareStorage.listKeys().keys[0].value
        }
      }
    ]
  }
}

output id string = qdrant.id
output name string = qdrant.name
// Stable private IP — the gRPC host the backend dials (port 6334).
output host string = qdrant.properties.ipAddress.ip
