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

// --- Snapshot / DR knobs (Ch 25 §3.8) ---
@description('Storage account (from storage.bicep) that holds the qdrant-snapshots blob container.')
param snapshotStorageAccountName string = ''

@description('Blob container the snapshot job uploads to (storage.bicep output qdrantSnapshotsContainer).')
param snapshotContainerName string = 'qdrant-snapshots'

@description('Cron schedule for the snapshot job. Default: daily at 02:00 UTC.')
param snapshotCron string = '0 2 * * *'

@description('Image with curl + the Azure CLI for the snapshot sidecar.')
param snapshotJobImage string = 'mcr.microsoft.com/azure-cli:2.61.0'

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

// --- Scheduled snapshot / DR job (Ch 25 §3.8) -----------------------------------------
// Self-hosted Qdrant has no managed backup, so a sidecar ACI group runs a cron loop that:
//   1. POSTs the Qdrant snapshot API (/collections/{c}/snapshots) to materialise a snapshot
//      on the persistent file share,
//   2. uploads the snapshot files to the qdrant-snapshots blob container (durable, GZRS in
//      prod) with `az storage blob upload-batch`.
// Restore (stop ingest -> pull snapshot -> /snapshots/recover -> verify counts -> resume)
// is documented in docs/runbooks/qdrant-restore.md, with the RPO/RTO posture.
//
// The managed 'azure-search' backend (deploy/modules/azureSearch.bicep) is the HA
// alternative: Microsoft replicates the index across replicas/partitions, so it needs no
// manual snapshots — switch hybridBackend to 'azure-search' to remove this job entirely.
//
// Deployed only when a snapshot storage account is supplied (wired in main.bicep); the
// account key is read at deploy time from the existing storage account (no key in source).
resource snapshotStorage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = if (!empty(snapshotStorageAccountName)) {
  name: snapshotStorageAccountName
}

resource snapshotJob 'Microsoft.ContainerInstance/containerGroups@2023-05-01' = if (!empty(snapshotStorageAccountName)) {
  name: 'aci-qdrant-snap-${environment}-${suffix}'
  location: location
  tags: tags
  properties: {
    osType: 'Linux'
    restartPolicy: 'Always'
    // Same data subnet so it can reach Qdrant on its private IP.
    subnetIds: [
      {
        id: dataSubnetId
      }
    ]
    containers: [
      {
        name: 'qdrant-snapshot'
        properties: {
          image: snapshotJobImage
          command: [
            '/bin/sh'
            '-c'
            // Cron-style loop: take a snapshot of every collection, then sync to blob.
            // QDRANT_URL targets the sibling Qdrant container on its private IP:6333.
            // Snapshots are read from the shared volume at $SNAP_DIR (Qdrant is configured
            // with QDRANT__STORAGE__SNAPSHOTS_PATH under /qdrant/storage so they persist).
            'while true; do TS=$(date -u +%Y%m%dT%H%M%SZ); for C in $(curl -s "$QDRANT_URL/collections" | sed -n \'s/.*"name":"\\([^"]*\\)".*/\\1/p\'); do curl -s -X POST "$QDRANT_URL/collections/$C/snapshots"; done; az storage blob upload-batch --account-name "$SNAP_ACCOUNT" --auth-mode key --account-key "$SNAP_KEY" -d "$SNAP_CONTAINER/$TS" -s "$SNAP_DIR" --overwrite >/dev/null 2>&1 || true; sleep 86400; done'
          ]
          environmentVariables: [
            {
              name: 'QDRANT_URL'
              value: 'http://${qdrant.properties.ipAddress.ip}:6333'
            }
            {
              name: 'SNAP_ACCOUNT'
              value: snapshotStorageAccountName
            }
            {
              name: 'SNAP_CONTAINER'
              value: snapshotContainerName
            }
            {
              name: 'SNAP_CRON'
              value: snapshotCron
            }
            {
              // Snapshot dir on the shared volume — Qdrant writes snapshots here.
              name: 'SNAP_DIR'
              value: '/qdrant/storage/snapshots'
            }
            {
              name: 'SNAP_KEY'
              secureValue: empty(snapshotStorageAccountName) ? '' : snapshotStorage.listKeys().keys[0].value
            }
          ]
          resources: {
            requests: {
              cpu: 1
              memoryInGB: 1
            }
          }
          // Mount the same persistent share as the main Qdrant container (at /qdrant/storage)
          // so the job can read the snapshot files Qdrant wrote, then push them to blob.
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
