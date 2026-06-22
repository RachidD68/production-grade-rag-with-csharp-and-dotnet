// Microsoft.KeyVault/vaults — the SmartDocs secret store (connection strings, API keys, signing keys, OAuth secrets); RBAC auth, private-endpoint hook.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Data subnet id — where the Key Vault private endpoint lands.')
param dataSubnetId string = ''

@description('Create a private endpoint for the vault (off in dev to keep the loop fast).')
param enablePrivateEndpoint bool = false

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
// Key Vault names are globally unique, 3-24 chars, alphanumeric + dashes.
var vaultName = take('kv-sd-${environment}-${suffix}', 24)

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    // RBAC authorization — no access policies, identities get roles instead.
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: enablePrivateEndpoint ? 'Disabled' : 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: enablePrivateEndpoint ? 'Deny' : 'Allow'
    }
  }
}

// Private endpoint hook — lands the vault in the data subnet when enabled.
resource privateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = if (enablePrivateEndpoint && !empty(dataSubnetId)) {
  name: 'pe-${vaultName}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: dataSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'kv-connection'
        properties: {
          privateLinkServiceId: keyVault.id
          groupIds: [ 'vault' ]
        }
      }
    ]
  }
}

output id string = keyVault.id
output name string = keyVault.name
output uri string = keyVault.properties.vaultUri
