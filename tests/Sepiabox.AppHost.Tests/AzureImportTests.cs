using System.Text.RegularExpressions;

namespace Sepiabox.AppHost.Tests;

/// <summary>Manifests imported when they arrive (S-033), checked on the published Bicep and manifest.</summary>
public sealed class AzureImportTests(PublishedInfrastructure published) : IClassFixture<PublishedInfrastructure>
{
    private const string Job = "Microsoft.App/jobs";

    [Fact]
    public void Namespace_and_queue_are_the_ones_the_shutdown_runbook_disables_when_published()
    {
        var serviceBus = published.The("Microsoft.ServiceBus/namespaces");
        var queue = published.The("Microsoft.ServiceBus/namespaces/queues");

        Assert.Equal("'" + ShutdownRunbook.ServiceBusNamespace + "'", serviceBus.Property("name"));
        Assert.Equal("'" + ShutdownRunbook.Queue + "'", queue.Property("name"));
        Assert.Equal("'francecentral'", serviceBus.Property("location"));
        Assert.Contains("name: 'Basic'", serviceBus.Block("sku"), StringComparison.Ordinal);
        Assert.Equal("true", serviceBus.Property("disableLocalAuth"));
    }

    [Fact]
    public void Queue_dead_letters_a_message_after_five_deliveries_when_published()
    {
        var queue = published.The("Microsoft.ServiceBus/namespaces/queues");

        Assert.Equal("5", queue.Property("maxDeliveryCount"));
        Assert.Equal("true", queue.Property("deadLetteringOnMessageExpiration"));
    }

    [Fact]
    public void Only_blobs_created_in_the_manifests_container_reach_the_queue_when_published()
    {
        var topic = published.The("Microsoft.EventGrid/systemTopics");
        var subscription = published.The("Microsoft.EventGrid/systemTopics/eventSubscriptions");

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
        Assert.Contains("namespace: 'sepiabox-sb'", trigger, StringComparison.Ordinal);
        Assert.Contains("queueName: 'manifests'", trigger, StringComparison.Ordinal);
        // KEDA reads the queue as the jobs' identity, with no connection string.
        Assert.Contains("identity: jobs_identity_id", trigger, StringComparison.Ordinal);
        Assert.Equal("{jobs-identity.outputs.id}", parameters["jobs_identity_id"]!.GetValue<string>());
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

    [Theory]
    [InlineData("import")]
    [InlineData("import-all")]
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

        Assert.Equal("sepiabox-sb.servicebus.windows.net", environment["ServiceBus__Namespace"]!.GetValue<string>());
        Assert.Equal("manifests", environment["ServiceBus__Queue"]!.GetValue<string>());
    }

    [Fact]
    public void Every_worker_job_uses_the_one_jobs_identity_when_published()
    {
        foreach (var file in new[] { "migrate", "import", "import-all" }.Select(r => r + "-containerapp.module.bicep"))
        {
            Assert.Matches(new Regex(@"\{jobs-identity\.outputs\.id\}"), published.ModuleParameters[file].ToJsonString());
        }
    }

    private string[] Arguments(string resource) =>
        [.. published.Resource(resource)["args"]!.AsArray().Select(a => a!.GetValue<string>())];
}
