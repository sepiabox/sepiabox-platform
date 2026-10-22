// Access Aspire's role helpers cannot scope (S-058, D-062): the web's write access to the data protection
// container only, its use of the data protection key only, the faces job's write access to the manifests container
// only (S-022), the sync job's access to incoming, the key ring and the key (S-038), the process job's write access to the four containers it touches (S-036), the admin job's write
// access to the three containers it deletes from (S-057), and the uploaders' write access to storage.
// The account-wide roles (the web's and the worker's Storage Blob Data Reader, the web's Key Vault Secrets
// User) come from the AppHost's WithRoleAssignments.

@description('Not used: every resource here is a child of one that already has its region. azd passes it.')
param location string = resourceGroup().location

param storageName string

param vaultName string

@description('The web\'s managed identity.')
param webPrincipalId string

@description('The faces job\'s managed identity, which erases faces from the stored manifests (S-022).')
param facesPrincipalId string

@description('The process job\'s managed identity, which processes the files in incoming (S-036).')
param processPrincipalId string

@description('The sync job\'s managed identity, which stages Google Drive files in incoming and reads the web\'s key ring (S-038).')
param syncPrincipalId string
@description('The admin job\'s managed identity, which deletes a source\'s assets (S-057, D-108).')
param adminPrincipalId string

@description('Comma-separated Entra object ids of the people who run `sepiabox-ingest upload --destination azure`. User object ids only (`az ad signed-in-user show --query id -o tsv`), not groups or service principals.')
param uploaderPrincipalIds string = ''

// Trimmed, lower-cased (object ids are GUIDs) and without repeats, so one id listed twice is one assignment.
var uploaderIds = filter(map(split(uploaderPrincipalIds, ','), id => toLower(trim(id))), id => !empty(id))
var uploaders = filter(uploaderIds, (id, i) => indexOf(uploaderIds, id) == i)

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

resource manifestsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' existing = {
  parent: blobService
  name: 'manifests'
}

// The process job reads and deletes the files in incoming, copies originals, writes derivatives and writes manifests
// through ManifestStore (S-036): these four containers and nothing else.
resource incomingContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' existing = {
  parent: blobService
  name: 'incoming'
}

resource originalsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' existing = {
  parent: blobService
  name: 'originals'
}

resource derivativesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' existing = {
  parent: blobService
  name: 'derivatives'
}

resource tokenRingContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' existing = {
  parent: blobService
  name: 'tokenring'
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

// Wraps the token ring (S-038, D-115): the keys that protect the cloud accounts' refresh tokens, in a ring of their own so the sync job,
// which reads it, never holds what signs the sign-in cookies. The web writes the ring and the sync reads it.
resource tokenRingKey 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
  parent: vault
  name: 'tokenring'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource webTokenRingWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(tokenRingContainer.id, webPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: tokenRingContainer
  properties: {
    principalId: webPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

resource webTokenKeyUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(tokenRingKey.id, webPrincipalId, '12338af0-0e69-4776-bea7-57ae8d297424')
  scope: tokenRingKey
  properties: {
    principalId: webPrincipalId
    // Key Vault Crypto User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
    principalType: 'ServicePrincipal'
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

// Reads and rewrites manifests through ManifestStore, and nothing else in storage (S-022, D-081).
resource facesManifestsWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(manifestsContainer.id, facesPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: manifestsContainer
  properties: {
    principalId: facesPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

// The sync job (S-038, D-115): writes incoming, reads the token ring and uses the key that wraps it, and nothing else in storage. It has no
// role on the cookie ring or its key, and none on the owner's Key Vault here: scripts/deploy.sh gives it a role on each of the two secrets
// of the Google OAuth client, which Bicep cannot do for secrets the owner has not created yet.
resource syncIncomingWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(incomingContainer.id, syncPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: incomingContainer
  properties: {
    principalId: syncPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

resource syncKeyRingReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(tokenRingContainer.id, syncPrincipalId, '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')
  scope: tokenRingContainer
  properties: {
    principalId: syncPrincipalId
    // Storage Blob Data Reader
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')
    principalType: 'ServicePrincipal'
  }
}

resource syncKeyUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(tokenRingKey.id, syncPrincipalId, '12338af0-0e69-4776-bea7-57ae8d297424')
  scope: tokenRingKey
  properties: {
    principalId: syncPrincipalId
    // Key Vault Crypto User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
    principalType: 'ServicePrincipal'
  }
}

// The process job (S-036): the four containers it touches, each its own assignment.
resource processIncomingWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(incomingContainer.id, processPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: incomingContainer
  properties: {
    principalId: processPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

resource processOriginalsWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(originalsContainer.id, processPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: originalsContainer
  properties: {
    principalId: processPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

resource processDerivativesWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(derivativesContainer.id, processPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: derivativesContainer
  properties: {
    principalId: processPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

resource processManifestsWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(manifestsContainer.id, processPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: manifestsContainer
  properties: {
    principalId: processPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

// Deletes manifests, derivatives and originals, and nothing else in storage: not the web's key ring, not `incoming` (S-057).
resource adminManifestsWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(manifestsContainer.id, adminPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: manifestsContainer
  properties: {
    principalId: adminPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

resource adminDerivativesWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(derivativesContainer.id, adminPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: derivativesContainer
  properties: {
    principalId: adminPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalType: 'ServicePrincipal'
  }
}

resource adminOriginalsWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(originalsContainer.id, adminPrincipalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: originalsContainer
  properties: {
    principalId: adminPrincipalId
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
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

output tokenRingKeyUri string = tokenRingKey.properties.keyUri
