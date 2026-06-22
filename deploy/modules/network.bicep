// Microsoft.Network/virtualNetworks — the SmartDocs VNet: one VNet, three subnets (frontend/backend/data) with NSGs enforcing the inbound chain.

@description('Logical environment name (dev, staging, prod). Used as a name suffix.')
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('VNet address space.')
param addressPrefix string = '10.20.0.0/16'

@description('Frontend subnet prefix — App Gateway / public ingress lives here.')
param frontendPrefix string = '10.20.1.0/24'

@description('Backend subnet prefix — App Service, MCP, ingest worker.')
param backendPrefix string = '10.20.2.0/24'

@description('Data subnet prefix — Qdrant, Neo4j, private endpoints.')
param dataPrefix string = '10.20.3.0/24'

var tags = {
  environment: environment
  app: 'smartdocs'
}

var suffix = uniqueString(resourceGroup().id, environment)
var vnetName = 'vnet-smartdocs-${environment}-${suffix}'

// NSG: frontend accepts public HTTPS (App Gateway entry point).
resource frontendNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-frontend-${environment}-${suffix}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'Allow-HTTPS-Inbound'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: frontendPrefix
          destinationPortRange: '443'
        }
      }
    ]
  }
}

// NSG: backend accepts traffic only from the frontend subnet.
resource backendNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-backend-${environment}-${suffix}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'Allow-From-Frontend'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: frontendPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: backendPrefix
          destinationPortRange: '*'
        }
      }
      {
        name: 'Deny-Other-Inbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

// NSG: data accepts traffic only from the backend subnet.
resource dataNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-data-${environment}-${suffix}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'Allow-From-Backend'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: backendPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: dataPrefix
          destinationPortRange: '*'
        }
      }
      {
        name: 'Deny-Other-Inbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [ addressPrefix ]
    }
    subnets: [
      {
        name: 'frontend'
        properties: {
          addressPrefix: frontendPrefix
          networkSecurityGroup: { id: frontendNsg.id }
        }
      }
      {
        name: 'backend'
        properties: {
          addressPrefix: backendPrefix
          networkSecurityGroup: { id: backendNsg.id }
          // Container Apps environment needs the subnet delegated.
          delegations: [
            {
              name: 'containerapps-delegation'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'data'
        properties: {
          addressPrefix: dataPrefix
          networkSecurityGroup: { id: dataNsg.id }
          // Container Instances (Qdrant) needs the subnet delegated.
          delegations: [
            {
              name: 'aci-delegation'
              properties: {
                serviceName: 'Microsoft.ContainerInstance/containerGroups'
              }
            }
          ]
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output frontendSubnetId string = vnet.properties.subnets[0].id
output backendSubnetId string = vnet.properties.subnets[1].id
output dataSubnetId string = vnet.properties.subnets[2].id
