## S-033 — Import manifests automatically when they arrive
**Epic:** Cloud and costs
**Depends on:** S-032, S-008, S-050
**Priority:** High
**Size:** M

### Context
Every manifest sent from the PC must be imported without intervention and without risk of an infinite loop or runaway cost (D-008).

### Scope
- Service Bus Basic with a `manifests` queue (`maxDeliveryCount` 5, dead-letter).
- Event Grid: system topic for the Storage account; `BlobCreated` subscription filtered by `subjectBeginsWith: /blobServices/default/containers/manifests/` → queue.
- Worker as an event-driven Container Apps Job (KEDA `azure-servicebus`): parallelism 1, one execution at a time, 30 min timeout, 1 vCPU / 2 GiB; the `import --from-queue` command processes messages until the queue is empty.

### Out of scope
- Processing files without a manifest (S-036).

### Acceptance criteria
- [ ] Given an Event Grid BlobCreated payload, then the worker extracts the manifest name.
- [ ] Given an event from another container, then it is ignored.
- [ ] Given the generated Bicep, then the subscription has the `subjectBeginsWith` filter and the queue has `maxDeliveryCount` 5.
- [ ] Given the generated Bicep, then the job has parallelism 1.

### Technical notes
- `Azure.Messaging.ServiceBus`. Resources added via Aspire (`AddAzureServiceBus`) and `ConfigureInfrastructure` for Event Grid.
- Portability (D-051): the worker's import loop depends on `IBlobEventQueue` (receive a `BlobCreated(container, name)`, complete, abandon) in `Sepiabox.Storage`; the Service Bus receiver and the Event Grid payload parsing are its Azure implementation, and the import reads manifests through `IBlobStore` (S-050). Unit tests use an in-memory queue.
- Decisions: D-008, D-051.

### Expected tests
- Unit: event parsing, filter. Infrastructure generation test.
