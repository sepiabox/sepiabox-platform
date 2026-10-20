// Manifests imported when they arrive (S-033, D-063): the storage account's BlobCreated events for the
// `manifests` container go through Event Grid to the Service Bus queue, which wakes the import job.
// The namespace and the queue come from the AppHost (AddAzureServiceBus).

@description('Not used: the system topic must be in the storage account\'s region, pinned below (D-002). azd passes it.')
param location string = resourceGroup().location

param storageName string

param serviceBusName string

param queueName string

@description('The worker jobs\' managed identity, which receives from the queue.')
param jobsPrincipalId string

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageName
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusName
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' existing = {
  parent: serviceBus
  name: queueName
}

// One system topic per storage account; its identity delivers to the queue, which takes no keys.
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

resource jobsReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(queue.id, jobsPrincipalId, '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')
  scope: queue
  properties: {
    principalId: jobsPrincipalId
    // Azure Service Bus Data Receiver
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')
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
  // Event Grid checks that its identity may send before it accepts the subscription.
  dependsOn: [
    topicSender
  ]
}
