namespace Sepiabox.AppHost.Tests;

/// <summary>The job that loads GeoNames in Azure (S-059, D-067), checked on the published Bicep and manifest.</summary>
public sealed class AzureGeoNamesTests(PublishedInfrastructure published) : IClassFixture<PublishedInfrastructure>
{
    private const string Job = "Microsoft.App/jobs";

    [Fact]
    public void GeoNames_is_a_manual_job_of_the_worker_with_no_schedule_when_published()
    {
        var job = published.Named(Job, "sepiabox-geonames");
        var resource = published.Resource("geonames");

        Assert.Equal("'Manual'", job.Property("triggerType"));
        Assert.Contains("parallelism: 1", job.Block("manualTriggerConfig"), StringComparison.Ordinal);
        Assert.Contains("replicaCompletionCount: 1", job.Block("manualTriggerConfig"), StringComparison.Ordinal);
        Assert.DoesNotContain("scheduleTriggerConfig", job.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("cronExpression", job.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("eventTriggerConfig", job.Body, StringComparison.Ordinal);
        Assert.Equal("'francecentral'", job.Property("location"));
        // A few minutes of work, billed only while it runs (D-008); a stuck run is stopped.
        Assert.Equal("1800", job.Property("replicaTimeout"));
        Assert.Equal("0", job.Property("replicaRetryLimit"));
        Assert.Contains("cpu: 1", job.Block("resources"), StringComparison.Ordinal);
        Assert.Contains("memory: '2Gi'", job.Block("resources"), StringComparison.Ordinal);
        // The worker image, downloading, loading and naming places in one run.
        Assert.Matches(@"args: \[\s+'geonames'\s+'update'\s+\]", job.Body);
        Assert.Equal("{geonames.containerImage}", published.ModuleParameters["geonames-containerapp.module.bicep"]["geonames_containerimage"]!.GetValue<string>());
        Assert.EndsWith("Sepiabox.Worker.csproj", resource["path"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void GeoNames_job_connects_as_the_administrator_from_the_database_vault_with_migrate_s_identity_when_published()
    {
        // It writes geonames, which the application role may only read (D-066).
        var job = published.Named(Job, "sepiabox-geonames");
        var environment = published.Resource("geonames")["env"]!.AsObject();
        var parameters = published.ModuleParameters["geonames-containerapp.module.bicep"];

        Assert.Equal("{catalog.connectionString}", environment["ConnectionStrings__catalog"]!.GetValue<string>());
        Assert.Matches(@"name: 'ConnectionStrings__catalog'\s+secretRef: 'connectionstrings--catalog'", job.Body);
        Assert.Matches(@"name: 'connectionstrings--catalog'\s+identity: migrate_identity_outputs_id\s+keyVaultUrl: database_vault_", job.Body);
        Assert.Equal("{migrate-identity.outputs.id}", parameters["migrate_identity_outputs_id"]!.GetValue<string>());
        Assert.DoesNotContain("pg_password_value", job.Body, StringComparison.Ordinal);
        // Nothing else: no storage, no queue, no owner secrets, and it never migrates or touches the role.
        Assert.DoesNotContain(environment, e => e.Key.StartsWith("Catalog__AppRole", StringComparison.Ordinal));
        Assert.DoesNotContain(environment, e => e.Key.StartsWith("ConnectionStrings__", StringComparison.Ordinal) && e.Key != "ConnectionStrings__catalog");
        Assert.DoesNotContain(environment, e => e.Key.StartsWith("ServiceBus__", StringComparison.Ordinal));
    }

    [Fact]
    public void GeoNames_job_adds_no_role_of_its_own_when_published()
    {
        // Migrate's identity already reads the database vault; the job's role module declares nothing more
        // (AzureAccessTests lists every assignment).
        Assert.DoesNotContain("roleAssignments", published.Modules["geonames-roles-database-vault.module.bicep"], StringComparison.Ordinal);
    }
}
