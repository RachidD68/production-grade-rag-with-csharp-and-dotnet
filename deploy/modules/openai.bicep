// Microsoft.CognitiveServices/accounts (kind 'OpenAI') — the LLM + embeddings: gpt-5.6-terra and text-embedding-3-small deployments.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources. Pick a region that carries the models you deploy.')
param location string = resourceGroup().location

@description('Account SKU. S0 is pay-as-you-go; provisioned-throughput (PTU) is a separate SKU/commitment for prod.')
param skuName string = 'S0'

@description('Capacity (PTUs for provisioned SKUs; TPM-in-thousands for S0 deployments).')
param chatCapacity int = 30

@description('Embedding deployment capacity.')
param embeddingCapacity int = 30

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var accountName = take('oai-sd-${environment}-${suffix}', 64)

resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: skuName
  }
  properties: {
    customSubDomainName: accountName
    publicNetworkAccess: 'Enabled'
    // Prefer Entra ID tokens; disable shared-key auth in prod via param if desired.
    disableLocalAuth: false
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

// Chat model. Deployments must be serialized — the second depends on the first.
// gpt-4o / gpt-4o-mini are Deprecated on Azure OpenAI (no NEW deployments), so
// the chat deployment targets the gpt-5.6 family. `model.version` is omitted on
// purpose: the ARM contract documents it as optional ("if version is not
// specified, a default version will be assigned") and the default is only
// discoverable through the list-models API at deploy time — pinning a dated
// value here would bake in a string this repo cannot verify offline.
// OnceNewDefaultVersionAvailable keeps the deployment on the platform default.
resource chat 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-5.6-terra'
  sku: {
    name: skuName == 'S0' ? 'Standard' : 'ProvisionedManaged'
    capacity: chatCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-5.6-terra'
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
}

resource embeddings 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'text-embedding-3-small'
  sku: {
    name: skuName == 'S0' ? 'Standard' : 'ProvisionedManaged'
    capacity: embeddingCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-small'
      version: '1'
    }
    versionUpgradeOption: 'OnceCurrentVersionExpired'
  }
  // Azure OpenAI rejects concurrent deployment creates on one account.
  dependsOn: [ chat ]
}

output id string = openai.id
output name string = openai.name
output endpoint string = openai.properties.endpoint
output chatDeployment string = chat.name
output embeddingDeployment string = embeddings.name
