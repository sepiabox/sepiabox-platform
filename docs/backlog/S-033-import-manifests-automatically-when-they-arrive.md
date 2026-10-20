## S-033 — Import manifests automatically when they arrive
**Epic:** Cloud and costs
**Depends on:** S-032, S-008
**Priority:** High
**Size:** M

### Context
Every manifest sent from the PC must be imported without intervention and without risk of an infinite loop or runaway cost (D-008).

### Scope
- Service Bus Basic namespace `sepiabox-sb` with a `manifests` queue (`maxDeliveryCount` 5, dead-letter); the names S-030's shutdown runbook disables.
- Event Grid: system topic for the Storage account; `BlobCreated` subscription filtered by `subjectBeginsWith: /blobServices/default/containers/manifests/` → queue.
- Worker as an event-driven Container Apps Job (KEDA `azure-servicebus`): parallelism 1, one execution at a time, 30 min timeout, 1 vCPU / 2 GiB; the `import --from-queue` command processes messages until the queue is empty.
- Backfill and reconcile: a manual-trigger job `sepiabox-import-all` (same image) running `sepiabox-worker import --once`, for manifests uploaded before this story and after a message was dead-lettered; the import is idempotent (D-032, D-034).
- Worker job identity (both jobs): *Storage Blob Data Reader* on the storage account, *Azure Service Bus Data Receiver* on the `manifests` queue (event-driven job only), *Key Vault Secrets User* for the catalog connection.
- `docs/operations.md`: how to start `sepiabox-import-all`, and how to check the dead-letter count (`az servicebus queue show --namespace-name sepiabox-sb --name manifests --query countDetails.deadLetterMessageCount`) and what to do when it is not zero.

### Out of scope
- Processing files without a manifest (S-036).

### Acceptance criteria
- [ ] Given an Event Grid BlobCreated payload, then the worker extracts the manifest name.
- [ ] Given an event from another container, then it is ignored.
- [ ] Given the generated Bicep, then the subscription has the `subjectBeginsWith` filter and the queue has `maxDeliveryCount` 5.
- [ ] Given the generated Bicep, then the job has parallelism 1.
- [ ] Given the generated Bicep, then the namespace is `sepiabox-sb` and the queue `manifests` (a test compares them with S-030's runbook constants).
- [ ] Given the generated Bicep, then `sepiabox-import-all` exists with a manual trigger and the worker identity has exactly the roles in Scope.
- [ ] Given a real deploy (manual, owner acceptance in `docs/operations.md`), when I `upload` one folder to Azure, then its assets appear in the cloud catalog (the timeline at the Container Apps default host) without running anything else, and the dead-letter count stays 0.

### Technical notes
- `Azure.Messaging.ServiceBus`. Resources added via Aspire (`AddAzureServiceBus`) and `ConfigureInfrastructure` for Event Grid.
- Portability (D-051, new code only): the new queue loop depends on an `IBlobEventQueue` interface (receive a `BlobCreated(container, name)`, complete, abandon) declared in the worker; the Service Bus receiver and the Event Grid payload parsing are its Azure implementation, registered in `Program.cs`. Unit tests use an in-memory queue. The existing manifest reads stay as they are; S-050 (after the MVP) moves the interface to `Sepiabox.Storage`.
- Decisions: D-008, D-051.

### Expected tests
- Unit: event parsing, filter. Infrastructure generation test.
