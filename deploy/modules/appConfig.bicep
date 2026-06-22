// Microsoft.AppConfiguration/configurationStores — centralised configuration + feature
// management for SmartDocs (Ch 25 §3.7). Standard SKU (required for feature flags and
// the higher request quota). Local (access-key) auth is disabled: every reader
// authenticates with Entra ID and is granted the App Configuration Data Reader role
// (see main.bicep), so feature flags are evaluated at request scope via managed identity.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
// App Configuration store names are 5-50 chars, alphanumeric + hyphen.
var storeName = take('appcs-smartdocs-${environment}-${suffix}', 50)

resource appConfig 'Microsoft.AppConfiguration/configurationStores@2024-05-01' = {
  name: storeName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
  }
  properties: {
    // Entra-only: no connection-string / access-key auth. Readers use managed identity.
    disableLocalAuth: true
    // Soft-delete + purge protection so a misfire can't drop the flag store.
    enablePurgeProtection: false
    publicNetworkAccess: 'Enabled'
  }
}

output id string = appConfig.id
output name string = appConfig.name
// The endpoint the app reads feature flags / config from, resolving via managed identity.
output endpoint string = appConfig.properties.endpoint
