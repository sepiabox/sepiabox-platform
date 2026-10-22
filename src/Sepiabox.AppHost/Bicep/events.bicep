// Manifests imported when they arrive (S-033, D-063): the storage account's BlobCreated events for the
// `manifests` container go through Event Grid to the Service Bus queue, which wakes the import job. The same for the
// `incoming` container and its queue, which wakes the process job (S-036, D-107).
// The namespace and the queues come from the AppHost (AddAzureServiceBus).

@description('Not used: the system topic must be in the storage account\'s region, pinned below (D-002). azd passes it.')
param location string = resourceGroup().location

param storageName string

param serviceBusName string

param queueName string

param incomingQueueName string

@description('The import jobs\' managed identity, which receives from the queue and whose length KEDA reads.')
param importPrincipalId string

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageName
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusName
}

@description('The process job\'s managed identity, which receives from the incoming queue and whose length KEDA reads.')
param processPrincipalId string

resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' existing = {
  parent: serviceBus
  name: queueName
}

resource incomingQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' existing = {
  parent: serviceBus
  name: incomingQueueName
}

// A storage account can have only one system topic; its identity delivers to the queue, which takes no keys.
resource storageEvents 'Microsoft.EventGrid/systemTopics@2025-02-15' = {
  name: 'sepiabox-storage-events'
  location: 'francecentral'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    source: storage.id
    topicType: 'Microsoft.Storage.StorageAccounts'
  }
}

resource topicSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(queue.id, storageEvents.id, '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
  scope: queue
  properties: {
    principalId: storageEvents.identity.principalId
    // Azure Service Bus Data Sender
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
    principalType: 'ServicePrincipal'
  }
}

// KEDA's scaler reads the queue's length (its runtime properties), which needs the Manage claim: with
// Entra, Data Owner. Scoped to this one queue, it also covers receiving; nothing else in the namespace.
resource importOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(queue.id, importPrincipalId, '090c5cfd-751d-490a-894a-3ce6f1109419')
  scope: queue
  properties: {
    principalId: importPrincipalId
    // Azure Service Bus Data Owner
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419')
    principalType: 'ServicePrincipal'
  }
}

// Only new or overwritten blobs in `manifests`; derivatives and originals never reach the queue.
resource manifestsToQueue 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2025-02-15' = {
  parent: storageEvents
  name: 'manifests-to-queue'
  properties: {
    eventDeliverySchema: 'EventGridSchema'
    filter: {
      includedEventTypes: [
        'Microsoft.Storage.BlobCreated'
      ]
      subjectBeginsWith: '/blobServices/default/containers/manifests/'
    }
    deliveryWithResourceIdentity: {
      identity: {
        type: 'SystemAssigned'
      }
      destination: {
        endpointType: 'ServiceBusQueue'
        properties: {
          resourceId: queue.id
        }
      }
    }
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
  }
  // After its identity's role, so deliveries can succeed; the role can still take minutes to apply, so a
  // first provision may need a second run (docs/operations.md).
  dependsOn: [
    topicSender
  ]
}

// The same for the incoming queue (S-036): the topic sends, the process job receives and KEDA reads its length.
resource incomingTopicSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(incomingQueue.id, storageEvents.id, '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
  scope: incomingQueue
  properties: {
    principalId: storageEvents.identity.principalId
    // Azure Service Bus Data Sender
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
    principalType: 'ServicePrincipal'
  }
}

resource processOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(incomingQueue.id, processPrincipalId, '090c5cfd-751d-490a-894a-3ce6f1109419')
  scope: incomingQueue
  properties: {
    principalId: processPrincipalId
    // Azure Service Bus Data Owner
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419')
    principalType: 'ServicePrincipal'
  }
}

// Only new or overwritten blobs in `incoming`: a connector writes a file with its metadata, and the worker processes it.
resource incomingToQueue 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2025-02-15' = {
  parent: storageEvents
  name: 'incoming-to-queue'
  properties: {
    eventDeliverySchema: 'EventGridSchema'
    filter: {
      includedEventTypes: [
        'Microsoft.Storage.BlobCreated'
      ]
      subjectBeginsWith: '/blobServices/default/containers/incoming/'
    }
    deliveryWithResourceIdentity: {
      identity: {
        type: 'SystemAssigned'
      }
      destination: {
        endpointType: 'ServiceBusQueue'
        properties: {
          resourceId: incomingQueue.id
        }
      }
    }
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
  }
  dependsOn: [
    incomingTopicSender
  ]
}
