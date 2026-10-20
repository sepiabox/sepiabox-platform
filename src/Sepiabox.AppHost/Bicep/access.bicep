// Access Aspire's role helpers cannot scope (S-058, D-062): the web's write access to the data protection
// container only, its use of the data protection key only, and the uploaders' write access to storage.
// The account-wide roles (the web's and the worker's Storage Blob Data Reader, the web's Key Vault Secrets
// User) come from the AppHost's WithRoleAssignments.

@description('Not used: every resource here is a child of one that already has its region. azd passes it.')
param location string = resourceGroup().location

param storageName string

param vaultName string

@description('The web\'s managed identity.')
param webPrincipalId string

@description('Comma-separated Entra object ids of the people who run `sepiabox-ingest upload --destination azure`.')
param uploaderPrincipalIds string = ''

var uploaders = filter(map(split(uploaderPrincipalIds, ','), id => trim(id)), id => !empty(id))

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' existing = {
  parent: storage
  name: 'default'
}

resource keyRingContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' existing = {
  parent: blobService
  name: 'dataprotection'
}

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: vaultName
}

// Wraps the web's data protection keys; only the web may use it, and only to wrap and unwrap.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource webKeyRingWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyRingContainer.id, webPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: keyRingContainer
  properties: {
    principalId: webPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

resource webKeyUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, webPrincipalId, '12338af0-0e69-4776-bea7-57ae8d297424')
  scope: dataProtectionKey
  properties: {
    principalId: webPrincipalId
    // Key Vault Crypto User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
    principalType: 'ServicePrincipal'
  }
}

// Owner on the resource group grants no data access (docs/operations.md).
resource uploaderWriters 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for id in uploaders: {
    name: guid(storage.id, id, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    scope: storage
    properties: {
      principalId: id
      // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
      principalType: 'User'
    }
  }
]

output dataProtectionKeyUri string = dataProtectionKey.properties.keyUri
