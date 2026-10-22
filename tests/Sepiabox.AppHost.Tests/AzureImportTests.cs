namespace Sepiabox.AppHost.Tests;

/// <summary>Manifests imported when they arrive (S-033), checked on the published Bicep and manifest.</summary>
public sealed class AzureImportTests(PublishedInfrastructure published) : IClassFixture<PublishedInfrastructure>
{
    private const string Job = "Microsoft.App/jobs";

    [Fact]
    public void Namespace_and_queue_are_the_ones_the_shutdown_runbook_disables_when_published()
    {
        var serviceBus = published.The("Microsoft.ServiceBus/namespaces");
        var queue = published.Named("Microsoft.ServiceBus/namespaces/queues", ShutdownRunbook.Queue);

        Assert.Equal("'" + ShutdownRunbook.ServiceBusNamespace + "'", serviceBus.Property("name"));
        Assert.Equal("'" + ShutdownRunbook.Queue + "'", queue.Property("name"));
        Assert.Equal("'francecentral'", serviceBus.Property("location"));
        Assert.Contains("name: 'Basic'", serviceBus.Block("sku"), StringComparison.Ordinal);
        Assert.Equal("true", serviceBus.Property("disableLocalAuth"));
    }

    [Theory]
    [InlineData("-sb")]
    [InlineData("-mgmt")]
    public void Namespace_avoids_a_suffix_azure_reserves_when_published(string reserved)
    {
        // The first deploy failed with NamespaceUnavailable: Azure refuses Service Bus names ending in these.
        Assert.False(AzureInfrastructure.ServiceBusName.EndsWith(reserved, StringComparison.Ordinal));
    }

    [Fact]
    public void Queue_dead_letters_a_message_after_five_deliveries_when_published()
    {
        foreach (var name in new[] { "manifests", "incoming" })
        {
            var queue = published.Named("Microsoft.ServiceBus/namespaces/queues", name);

            Assert.Equal("5", queue.Property("maxDeliveryCount"));
            Assert.Equal("true", queue.Property("deadLetteringOnMessageExpiration"));
        }
    }

    [Fact]
    public void Only_blobs_created_in_the_manifests_container_reach_the_queue_when_published()
    {
        var topic = published.The("Microsoft.EventGrid/systemTopics");
        var subscription = published.Named("Microsoft.EventGrid/systemTopics/eventSubscriptions", "manifests-to-queue");

        Assert.Equal("'Microsoft.Storage.StorageAccounts'", topic.Property("topicType"));
        Assert.Equal("storage.id", topic.Property("source"));
        Assert.Equal("'francecentral'", topic.Property("location"));
        Assert.Equal("'/blobServices/default/containers/manifests/'", subscription.Property("subjectBeginsWith"));
        Assert.Matches(@"includedEventTypes: \[\s+'Microsoft.Storage.BlobCreated'\s+\]", subscription.Body);
        Assert.Equal("'EventGridSchema'", subscription.Property("eventDeliverySchema"));
        // Delivered with the topic's own identity to the queue the AppHost names.
        Assert.Equal("'ServiceBusQueue'", subscription.Property("endpointType"));
        Assert.Equal("queue.id", subscription.Property("resourceId"));
        Assert.Equal("manifests", published.ModuleParameters["events.bicep"]["queueName"]!.GetValue<string>());
    }

    [Fact]
    public void Only_blobs_created_in_the_incoming_container_reach_the_incoming_queue_when_published()
    {
        var subscription = published.Named("Microsoft.EventGrid/systemTopics/eventSubscriptions", "incoming-to-queue");

        Assert.Equal("'/blobServices/default/containers/incoming/'", subscription.Property("subjectBeginsWith"));
        Assert.Matches(@"includedEventTypes: \[\s+'Microsoft.Storage.BlobCreated'\s+\]", subscription.Body);
        Assert.Equal("'ServiceBusQueue'", subscription.Property("endpointType"));
        Assert.Equal("incomingQueue.id", subscription.Property("resourceId"));
        Assert.Equal("incoming", published.ModuleParameters["events.bicep"]["incomingQueueName"]!.GetValue<string>());
    }

    [Fact]
    public void Process_job_runs_one_execution_at_a_time_when_the_incoming_queue_has_a_message_when_published()
    {
        var job = published.Named(Job, "sepiabox-process");
        var trigger = job.Block("eventTriggerConfig");
        var parameters = published.ModuleParameters["process-containerapp.module.bicep"];

        Assert.Equal("'Event'", job.Property("triggerType"));
        Assert.Contains("parallelism: 1", trigger, StringComparison.Ordinal);
        Assert.Contains("maxExecutions: 1", trigger, StringComparison.Ordinal);
        Assert.Contains("type: 'azure-servicebus'", trigger, StringComparison.Ordinal);
        Assert.Contains("queueName: 'incoming'", trigger, StringComparison.Ordinal);
        Assert.Contains("identity: process_identity_id", trigger, StringComparison.Ordinal);
        Assert.Equal("{process-identity.outputs.id}", parameters["process_identity_id"]!.GetValue<string>());
        Assert.Equal("3600", job.Property("replicaTimeout"));
        Assert.Equal("0", job.Property("replicaRetryLimit"));
        Assert.Contains("memory: '4Gi'", job.Block("resources"), StringComparison.Ordinal);
        Assert.Equal(["process", "--from-queue"], Arguments("process"));
        var environment = published.Resource("process")["env"]!.AsObject();
        Assert.Equal("incoming", environment["ServiceBus__Queue"]!.GetValue<string>());
        Assert.Equal("5", environment["ServiceBus__MaxDeliveryCount"]!.GetValue<string>());
        Assert.Equal("true", environment["Processing__OriginalsCold"]!.GetValue<string>());
    }

    [Fact]
    public void The_process_job_is_built_from_the_worker_dockerfile_when_published()
    {
        var resource = published.Resource("process");

        Assert.Equal("container.v1", resource["type"]!.GetValue<string>());
        Assert.EndsWith("infra/worker/Dockerfile", resource["build"]!["dockerfile"]!.GetValue<string>().Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public void Import_job_runs_one_execution_at_a_time_when_the_queue_has_a_message_when_published()
    {
        var job = published.Named(Job, "sepiabox-import");
        var trigger = job.Block("eventTriggerConfig");
        var parameters = published.ModuleParameters["import-containerapp.module.bicep"];

        Assert.Equal("'Event'", job.Property("triggerType"));
        Assert.Contains("parallelism: 1", trigger, StringComparison.Ordinal);
        Assert.Contains("replicaCompletionCount: 1", trigger, StringComparison.Ordinal);
        Assert.Contains("minExecutions: 0", trigger, StringComparison.Ordinal);
        Assert.Contains("maxExecutions: 1", trigger, StringComparison.Ordinal);
        Assert.Contains("type: 'azure-servicebus'", trigger, StringComparison.Ordinal);
        Assert.Contains("namespace: 'sepiabox-bus'", trigger, StringComparison.Ordinal);
        Assert.Contains("queueName: 'manifests'", trigger, StringComparison.Ordinal);
        // KEDA reads the queue as the import identity, with no connection string.
        Assert.Contains("identity: import_identity_id", trigger, StringComparison.Ordinal);
        Assert.Equal("{import-identity.outputs.id}", parameters["import_identity_id"]!.GetValue<string>());
        Assert.DoesNotContain("auth:", trigger, StringComparison.Ordinal);
        Assert.Equal("1800", job.Property("replicaTimeout"));
        Assert.Equal("0", job.Property("replicaRetryLimit"));
        Assert.Contains("cpu: 1", job.Block("resources"), StringComparison.Ordinal);
        Assert.Contains("memory: '2Gi'", job.Block("resources"), StringComparison.Ordinal);
    }

    [Fact]
    public void Import_all_is_a_manual_job_running_import_once_when_published()
    {
        var job = published.Named(Job, "sepiabox-import-all");

        Assert.Equal("'Manual'", job.Property("triggerType"));
        Assert.Contains("parallelism: 1", job.Block("manualTriggerConfig"), StringComparison.Ordinal);
        Assert.Equal("1800", job.Property("replicaTimeout"));
        Assert.Equal(["import", "--once"], Arguments("import-all"));
        Assert.Equal(["import", "--from-queue"], Arguments("import"));
    }

    [Fact]
    public void Faces_job_sweeps_the_manifests_every_hour_when_published()
    {
        var job = published.Named(Job, "sepiabox-faces");
        var trigger = job.Block("scheduleTriggerConfig");

        Assert.Equal("'Schedule'", job.Property("triggerType"));
        Assert.Contains("cronExpression: '17 * * * *'", trigger, StringComparison.Ordinal);
        Assert.Contains("parallelism: 1", trigger, StringComparison.Ordinal);
        Assert.Equal("0", job.Property("replicaRetryLimit"));
        Assert.Equal(["faces", "sweep"], Arguments("faces"));
    }

    [Fact]
    public void Sync_job_syncs_the_connected_drives_every_day_when_published()
    {
        var job = published.Named(Job, "sepiabox-sync");
        var trigger = job.Block("scheduleTriggerConfig");

        Assert.Equal("'Schedule'", job.Property("triggerType"));
        Assert.Contains("cronExpression: '27 3 * * *'", trigger, StringComparison.Ordinal);
        Assert.Contains("parallelism: 1", trigger, StringComparison.Ordinal);
        Assert.Equal("0", job.Property("replicaRetryLimit"));
        Assert.Equal(["sync"], Arguments("sync"));
        // It unprotects the tokens with the token ring, the URI of the key that wraps it, and has no setting for the cookie ring's key.
        var environment = published.Resource("sync")["env"]!.AsObject();
        Assert.Equal("{access.outputs.tokenRingKeyUri}", environment["DataProtection__TokenKeyUri"]!.GetValue<string>());
        Assert.False(environment.ContainsKey("DataProtection__KeyUri"));
        Assert.Equal("{access.outputs.tokenRingKeyUri}", published.Resource("web")["env"]!["DataProtection__TokenKeyUri"]!.GetValue<string>());
    }

    [Fact]
    public void Admin_job_is_started_by_hand_and_never_retried_with_its_own_identity_when_published()
    {
        var job = published.Named(Job, "sepiabox-admin");
        var parameters = published.ModuleParameters["admin-containerapp.module.bicep"].ToJsonString();

        Assert.Equal("'Manual'", job.Property("triggerType"));
        Assert.Contains("parallelism: 1", job.Block("manualTriggerConfig"), StringComparison.Ordinal);
        Assert.Equal("7200", job.Property("replicaTimeout"));
        Assert.Equal("0", job.Property("replicaRetryLimit"));
        Assert.Equal(["assets", "delete"], Arguments("admin"));
        Assert.Contains("{admin-identity.outputs.id}", parameters, StringComparison.Ordinal);
        Assert.DoesNotContain("import-identity", parameters, StringComparison.Ordinal);
        Assert.DoesNotContain("migrate-identity", parameters, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("import")]
    [InlineData("import-all")]
    [InlineData("faces")]
    [InlineData("sync")]
    [InlineData("admin")]
    public void Import_jobs_never_migrate_and_connect_as_the_application_role_when_published(string resource)
    {
        // D-063: only sepiabox-migrate changes the schema and grants the role its tables.
        var environment = published.Resource(resource)["env"]!.AsObject();

        Assert.DoesNotContain("--migrate", Arguments(resource));
        Assert.DoesNotContain(environment, e => e.Key.StartsWith("Catalog__AppRole", StringComparison.Ordinal));
        Assert.Equal(
            "Host=sepiabox-pg.postgres.database.azure.com;Username=sepiabox_web;Password={pg-app-password.value};Database=catalog",
            environment["ConnectionStrings__catalog"]!.GetValue<string>());
        Assert.Equal("{blobs.connectionString}", environment["ConnectionStrings__blobs"]!.GetValue<string>());
        Assert.EndsWith("Sepiabox.Worker.csproj", published.Resource(resource)["path"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Import_job_reads_the_queue_it_is_woken_for_when_published()
    {
        var environment = published.Resource("import")["env"]!.AsObject();

        Assert.Equal("sepiabox-bus.servicebus.windows.net", environment["ServiceBus__Namespace"]!.GetValue<string>());
        Assert.Equal("manifests", environment["ServiceBus__Queue"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("migrate", "migrate-identity")]
    [InlineData("import", "import-identity")]
    [InlineData("import-all", "import-identity")]
    [InlineData("geonames", "migrate-identity")]
    [InlineData("faces", "faces-identity")]
    [InlineData("sync", "sync-identity")]
    [InlineData("admin", "admin-identity")]
    public void Migrate_and_import_jobs_run_as_separate_identities_when_published(string job, string identity)
    {
        // Only migrate's identity reads the database vault; only the import identity reads storage and the
        // queue (the exact roles are in AzureAccessTests).
        var parameters = published.ModuleParameters[job + "-containerapp.module.bicep"].ToJsonString();
        var other = identity == "migrate-identity" ? "import-identity" : "migrate-identity";

        Assert.Contains("{" + identity + ".outputs.id}", parameters, StringComparison.Ordinal);
        Assert.DoesNotContain(other, parameters, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_job_dead_letters_after_the_queue_s_delivery_count_when_published()
    {
        var environment = published.Resource("import")["env"]!.AsObject();

        Assert.Equal("5", environment["ServiceBus__MaxDeliveryCount"]!.GetValue<string>());
        Assert.Equal("5", published.Named("Microsoft.ServiceBus/namespaces/queues", "manifests").Property("maxDeliveryCount"));
    }

    private string[] Arguments(string resource) =>
        [.. published.Resource(resource)["args"]!.AsArray().Select(a => a!.GetValue<string>())];
}
