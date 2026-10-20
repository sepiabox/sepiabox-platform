using Aspire.Hosting.Azure;
using Azure.Core;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ApplicationInsights;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.PostgreSql;
using Azure.Provisioning.Roles;
using Azure.Provisioning.Storage;

namespace Sepiabox.AppHost;

/// <summary>
/// How the AppHost's resources are published to Azure (S-032): the cheap and secure settings of D-002,
/// D-003, D-004 and D-022, and the names the €45 shutdown runbook in sepiabox-infra stops.
/// </summary>
public static class AzureInfrastructure
{
    /// <summary>Every resource is pinned here, whatever region the azd environment names (D-002).</summary>
    public static readonly AzureLocation Region = AzureLocation.FranceCentral;

    /// <summary>The web Container App; the shutdown runbook disables its ingress (S-030).</summary>
    public const string WebAppName = "sepiabox-web";

    /// <summary>The PostgreSQL server; the shutdown runbook stops it (S-030).</summary>
    public const string PostgresServerName = "sepiabox-pg";

    /// <summary>The Container Apps job that runs <c>sepiabox-worker migrate</c> on every deploy.</summary>
    public const string MigrateJobName = "sepiabox-migrate";

    /// <summary>The AppHost resource behind <see cref="MigrateJobName"/>, the name azd deploys it by.</summary>
    public const string MigrateResourceName = "migrate";

    /// <summary>Created by sepiabox-infra (S-031) with the daily cap; Application Insights writes to it.</summary>
    public const string LogAnalyticsWorkspaceName = "sepiabox-logs";

    /// <summary>
    /// Blob containers, all private. <c>originals</c>, <c>derivatives</c> and <c>manifests</c> are the
    /// <c>BlobLayout</c> containers; <c>dataprotection</c> holds the web's key ring (S-058).
    /// </summary>
    public static readonly IReadOnlyList<string> BlobContainers =
        ["originals", "derivatives", "manifests", "incoming", "exports", "dataprotection"];

    /// <summary>How long a deleted or overwritten blob can be restored.</summary>
    public const int BlobSoftDeleteDays = 7;

    /// <summary>
    /// The PostgreSQL administrator login azd generates once: letters only, which Azure accepts as a login.
    /// </summary>
    public static GenerateParameterDefault PostgresLogin { get; } = new()
    {
        MinLength = 12,
        Numeric = false,
        Special = false,
    };

    /// <summary>
    /// The PostgreSQL password azd generates once. No special characters: Aspire writes it unquoted into the
    /// Npgsql connection string, where a <c>;</c> or a quote would break it. Upper and lower case letters and
    /// digits are three of the four character classes Azure requires three of.
    /// </summary>
    public static GenerateParameterDefault PostgresPassword { get; } = new()
    {
        MinLength = 32,
        Special = false,
        MinLower = 1,
        MinUpper = 1,
        MinNumeric = 1,
    };

    // The migrations of a catalog this size take seconds; ten minutes is plenty and bounds a stuck run.
    private const int MigrateTimeoutSeconds = 600;

    // PostgreSQL 17 needs an API version newer than the 2024-08-01 the provisioning library defaults to.
    private const string PostgresApiVersion = "2025-08-01";

    public static void ConfigurePostgres(AzureResourceInfrastructure infrastructure)
    {
        var server = infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single();
        server.ResourceVersion = PostgresApiVersion;
        server.Name = PostgresServerName;
        server.Location = Region;
        server.Sku = new PostgreSqlFlexibleServerSku
        {
            Name = "Standard_B1ms",
            Tier = PostgreSqlFlexibleServerSkuTier.Burstable,
        };
        server.Storage = new PostgreSqlFlexibleServerStorage
        {
            StorageSizeInGB = 32,
            // Growing the disk can never be undone and raises the bill; a full disk should be a decision.
            AutoGrow = StorageAutoGrow.Disabled,
        };
        server.Version = new StringLiteralExpression("17");

        // Azure only lets a database create the extensions listed here (D-003).
        var extensions = new PostgreSqlFlexibleServerConfiguration("extensions")
        {
            Parent = server,
            Name = "azure.extensions",
            Value = "VECTOR,POSTGIS",
            Source = "user-override",
        };
        infrastructure.Add(extensions);

        // A flexible server takes one change at a time, so its children wait for the configuration.
        foreach (var database in infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServerDatabase>())
        {
            database.DependsOn.Add(extensions);
        }

        foreach (var rule in infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServerFirewallRule>())
        {
            rule.DependsOn.Add(extensions);
        }
    }

    public static void ConfigureStorage(AzureResourceInfrastructure infrastructure)
    {
        var account = infrastructure.GetProvisionableResources().OfType<StorageAccount>().Single();
        account.Location = Region;
        // Locally redundant: the originals stay on the drive, and geo-redundancy doubles the bill (D-061).
        account.Sku = new StorageSku { Name = StorageSkuName.StandardLrs };
        // Browsers only read through short-lived SAS links (D-004).
        account.AllowBlobPublicAccess = false;

        var blobService = infrastructure.GetProvisionableResources().OfType<BlobService>().SingleOrDefault();
        if (blobService is null)
        {
            blobService = new BlobService("blobService") { Parent = account };
            infrastructure.Add(blobService);
        }

        // A deleted or overwritten blob can be undeleted for a week: a mistaken delete (S-057) or a bad
        // manifest merge is recoverable, and a week of soft-deleted data costs little next to the library.
        blobService.DeleteRetentionPolicy = new DeleteRetentionPolicy
        {
            IsEnabled = true,
            Days = BlobSoftDeleteDays,
        };

        foreach (var name in BlobContainers)
        {
            infrastructure.Add(new BlobContainer(name + "Container")
            {
                Parent = blobService,
                Name = name,
                PublicAccess = StoragePublicAccessType.None,
            });
        }
    }

    public static void ConfigureKeyVault(AzureResourceInfrastructure infrastructure)
    {
        var vault = infrastructure.GetProvisionableResources().OfType<KeyVaultService>().Single();
        vault.Location = Region;
    }

    public static void ConfigureApplicationInsights(AzureResourceInfrastructure infrastructure)
    {
        var insights = infrastructure.GetProvisionableResources().OfType<ApplicationInsightsComponent>().Single();
        insights.Location = Region;
    }

    public static void ConfigureContainerRegistry(AzureResourceInfrastructure infrastructure)
    {
        var registry = infrastructure.GetProvisionableResources().OfType<ContainerRegistryService>().Single();
        registry.Location = Region;
        registry.Sku = new ContainerRegistrySku { Name = ContainerRegistrySkuName.Basic };
    }

    public static void ConfigureContainerAppEnvironment(AzureResourceInfrastructure infrastructure)
    {
        foreach (var environment in infrastructure.GetProvisionableResources().OfType<ContainerAppManagedEnvironment>())
        {
            environment.Location = Region;
        }

        // The identity the apps pull images with.
        foreach (var identity in infrastructure.GetProvisionableResources().OfType<UserAssignedIdentity>())
        {
            identity.Location = Region;
        }
    }

    public static void ConfigureIdentity(AzureResourceInfrastructure infrastructure)
    {
        var identity = infrastructure.GetProvisionableResources().OfType<UserAssignedIdentity>().Single();
        identity.Location = Region;
    }

    public static void ConfigureWebApp(AzureResourceInfrastructure infrastructure, ContainerApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Name = WebAppName;
        app.Location = Region;

        // Scales to zero when nobody uses it (D-008); one replica is enough for two people.
        app.Template.Scale.MinReplicas = 0;
        app.Template.Scale.MaxReplicas = 1;

        // The ingress is the only way in, over HTTPS only: the web trusts its X-Forwarded-For and
        // X-Forwarded-Proto for that reason (D-048).
        var ingress = app.Configuration.Ingress;
        ingress.External = true;
        ingress.AllowInsecure = false;
        ingress.Transport = ContainerAppIngressTransportMethod.Http;
    }

    public static void ConfigureMigrateJob(AzureResourceInfrastructure infrastructure, ContainerAppJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.Name = MigrateJobName;
        job.Location = Region;

        var configuration = job.Configuration;
        configuration.TriggerType = ContainerAppJobTriggerType.Manual;
        configuration.ManualTriggerConfig = new JobConfigurationManualTriggerConfig
        {
            Parallelism = 1,
            ReplicaCompletionCount = 1,
        };
        configuration.ReplicaTimeout = MigrateTimeoutSeconds;
        // A failed migration stops the deploy; running it again unattended would hide why.
        configuration.ReplicaRetryLimit = 0;
    }
}
