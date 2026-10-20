using Aspire.Hosting.Azure;
using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ApplicationInsights;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.PostgreSql;
using Azure.Provisioning.Roles;
using Azure.Provisioning.ServiceBus;
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

    /// <summary>The PostgreSQL server's host name, which Azure derives from its name.</summary>
    public const string PostgresHostName = PostgresServerName + ".postgres.database.azure.com";

    /// <summary>The Container Apps job that runs <c>sepiabox-worker migrate</c> on every deploy.</summary>
    public const string MigrateJobName = "sepiabox-migrate";

    /// <summary>The AppHost resource behind <see cref="MigrateJobName"/>, the name azd deploys it by.</summary>
    public const string MigrateResourceName = "migrate";

    /// <summary>The event-driven job that imports the manifests named by the queue (S-033).</summary>
    public const string ImportJobName = "sepiabox-import";

    /// <summary>The AppHost resource behind <see cref="ImportJobName"/>.</summary>
    public const string ImportResourceName = "import";

    /// <summary>The manual job that imports every new or changed manifest: backfill and reconcile (S-033).</summary>
    public const string ImportAllJobName = "sepiabox-import-all";

    /// <summary>The AppHost resource behind <see cref="ImportAllJobName"/>.</summary>
    public const string ImportAllResourceName = "import-all";

    /// <summary>The manual job that downloads GeoNames, loads it and names places (S-059).</summary>
    public const string GeoNamesJobName = "sepiabox-geonames";

    /// <summary>The AppHost resource behind <see cref="GeoNamesJobName"/>.</summary>
    public const string GeoNamesResourceName = "geonames";

    /// <summary>The Service Bus namespace; the shutdown runbook disables its queue (S-030).</summary>
    public const string ServiceBusName = "sepiabox-sb";

    /// <summary>The namespace's host name, which the import job connects to.</summary>
    public const string ServiceBusHostName = ServiceBusName + ".servicebus.windows.net";

    /// <summary>The queue Event Grid sends manifest events to; the shutdown runbook disables it (S-030).</summary>
    public const string ManifestsQueueName = "manifests";

    /// <summary>A message that fails this many times goes to the dead-letter queue (docs/operations.md).</summary>
    public const int ManifestsMaxDeliveryCount = 5;

    /// <summary>Created by sepiabox-infra (S-031) with the daily cap; Application Insights writes to it.</summary>
    public const string LogAnalyticsWorkspaceName = "sepiabox-logs";

    /// <summary>
    /// Blob containers, all private. <c>originals</c>, <c>derivatives</c> and <c>manifests</c> are the
    /// <c>BlobLayout</c> containers; <c>dataprotection</c> holds the web's key ring (S-058).
    /// </summary>
    public static readonly IReadOnlyList<string> BlobContainers =
        ["originals", "derivatives", "manifests", "incoming", "exports", "dataprotection"];

    /// <summary>
    /// The least-privilege PostgreSQL role the web connects with; <c>sepiabox-worker migrate</c> creates it
    /// (S-058, D-062).
    /// </summary>
    public const string AppRoleName = "sepiabox_web";

    /// <summary>
    /// The azd parameter with the Entra object ids (comma-separated) of the people who upload (S-058).
    /// </summary>
    public const string UploaderPrincipalIdsParameter = "uploaderPrincipalIds";

    /// <summary>
    /// Where "today" is for the search's "on this day" (S-013, D-069): the family's time zone, not the container's UTC.
    /// </summary>
    public const string SearchTimeZone = "Europe/Lisbon";

    /// <summary>The azd parameter with the web's custom domain, empty for none (S-035).</summary>
    public const string CustomDomainParameter = "customDomain";

    /// <summary>The azd parameter that binds the custom domain: <c>Disabled</c> first, <c>SniEnabled</c> once its DNS is in place.</summary>
    public const string CustomDomainBindingParameter = "customDomainBinding";

    /// <summary>The environment's output with the custom domain verification id (the TXT record <c>asuid.app</c>).</summary>
    public const string CustomDomainVerificationIdOutput = "AZURE_CONTAINER_APPS_CUSTOM_DOMAIN_VERIFICATION_ID";

    /// <summary>The managed certificate of the custom domain, in the Container Apps environment.</summary>
    public const string CustomDomainCertificateName = "sepiabox-custom-domain";

    /// <summary>How long a deleted or overwritten blob can be restored.</summary>
    public const int BlobSoftDeleteDays = 7;

    /// <summary>
    /// The PostgreSQL administrator login azd generates once: lower case letters only, which Azure accepts as a login.
    /// </summary>
    public static GenerateParameterDefault PostgresLogin { get; } = new()
    {
        MinLength = 12,
        Upper = false,
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
        MinLength = 64,
        Special = false,
        MinLower = 1,
        MinUpper = 1,
        MinNumeric = 1,
    };

    private const string AutoConfigureDataProtectionProperty = "AutoConfigureDataProtection";

    // An import of the whole drive's backlog by import-all, or a long queue, fits in half an hour; a stuck
    // run is stopped then, and its messages come back.
    private const int ImportTimeoutSeconds = 1800;

    // KEDA checks the queue this often; a manifest waits at most about this long before the job starts.
    private const int ImportPollingSeconds = 30;

    // The migrations of a catalog this size take seconds; ten minutes is plenty and bounds a stuck run.
    private const int MigrateTimeoutSeconds = 600;

    // Downloading about 10 MB, loading about 200,000 cities and naming the assets without a place take
    // minutes; half an hour bounds a stuck run.
    private const int GeoNamesTimeoutSeconds = 1800;

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
        foreach (var environment in infrastructure.GetProvisionableResources().OfType<ContainerAppManagedEnvironment>().ToList())
        {
            environment.Location = Region;

            // The value of the TXT record asuid.<subdomain> that proves the custom domain is ours (S-035).
            infrastructure.Add(new ProvisioningOutput(CustomDomainVerificationIdOutput, typeof(string))
            {
                Value = environment.CustomDomainConfiguration.CustomDomainVerificationId,
            });
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

    /// <summary>
    /// The web app, with the optional custom domain (S-035, D-065): none while <c>customDomain</c> is empty; the
    /// host name with its binding disabled while <c>customDomainBinding</c> is <c>Disabled</c>; bound with SNI to
    /// the managed certificate <c>Bicep/domain.bicep</c> creates once it is <c>SniEnabled</c>.
    /// </summary>
    public static Action<AzureResourceInfrastructure, ContainerApp> ConfigureWebApp(
        IResourceBuilder<ParameterResource> customDomain, IResourceBuilder<ParameterResource> customDomainBinding,
        BicepOutputReference environmentId) =>
        (infrastructure, app) =>
        {
            ConfigureWebApp(infrastructure, app);

            // The names Aspire gives the same values elsewhere in the module, so each is one parameter.
            var domain = customDomain.AsProvisioningParameter(infrastructure, "customdomain_value").BicepIdentifier;
            var binding = customDomainBinding.AsProvisioningParameter(infrastructure, "customdomainbinding_value").BicepIdentifier;
            var environment = environmentId
                .AsProvisioningParameter(infrastructure, "aca_outputs_azure_container_apps_environment_id").BicepIdentifier;
            // One conditional expression for the whole list: Azure.Provisioning has no conditional elements.
            ((IBicepValue)app.Configuration.Ingress.CustomDomains).Assign(new BicepValue<string>(new IdentifierExpression(
                $"empty({domain}) ? [] : [{{ name: {domain}, bindingType: {binding}, "
                + $"certificateId: {binding} == 'SniEnabled' ? '${{{environment}}}/managedCertificates/{CustomDomainCertificateName}' : null }}]")));
        };

    private static void ConfigureWebApp(AzureResourceInfrastructure infrastructure, ContainerApp app)
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

        // The web keeps its own key ring in Blob Storage under a Key Vault key (S-058, D-062); Container
        // Apps' managed data protection, which Aspire turns on, would be a second one.
        // Aspire adds the setting as a property of its own (runtime.dotnet.autoConfigureDataProtection), with no
        // typed accessor.
        app.Configuration.ProvisionableProperties[AutoConfigureDataProtectionProperty].Assign(new BicepValue<bool>(false));
    }

    public static void ConfigureServiceBus(AzureResourceInfrastructure infrastructure)
    {
        var serviceBus = infrastructure.GetProvisionableResources().OfType<ServiceBusNamespace>().Single();
        serviceBus.Name = ServiceBusName;
        serviceBus.Location = Region;
        // Basic has queues and dead-lettering, which is all the import needs, at a few cents a month (D-008).
        serviceBus.Sku = new ServiceBusSku { Name = ServiceBusSkuName.Basic, Tier = ServiceBusSkuTier.Basic };

        var queue = infrastructure.GetProvisionableResources().OfType<ServiceBusQueue>().Single();
        queue.MaxDeliveryCount = ManifestsMaxDeliveryCount;
        // The longest a message is locked while its manifest is imported, the most Service Bus allows.
        queue.LockDuration = TimeSpan.FromMinutes(5);
        queue.DeadLetteringOnMessageExpiration = true;
    }

    /// <summary>
    /// The event-driven import (S-033): KEDA starts one execution when the queue has a message, with the import
    /// identity, and the execution imports until the queue is empty. One at a time, so two imports never
    /// race on the same asset.
    /// </summary>
    public static Action<AzureResourceInfrastructure, ContainerAppJob> ConfigureImportJob(BicepOutputReference importIdentityId) =>
        (infrastructure, job) =>
        {
            ArgumentNullException.ThrowIfNull(job);
            job.Name = ImportJobName;
            ConfigureImportJobCommon(job);

            var configuration = job.Configuration;
            configuration.TriggerType = ContainerAppJobTriggerType.Event;
            configuration.EventTriggerConfig = new EventTriggerConfiguration
            {
                Parallelism = 1,
                ReplicaCompletionCount = 1,
                Scale = new ContainerAppJobScale
                {
                    MinExecutions = 0,
                    MaxExecutions = 1,
                    PollingIntervalInSeconds = ImportPollingSeconds,
                    Rules =
                    {
                        new ContainerAppJobScaleRule
                        {
                            Name = "manifests-queue",
                            JobScaleRuleType = "azure-servicebus",
                            Metadata = new ObjectExpression(
                                new PropertyExpression("namespace", new StringLiteralExpression(ServiceBusName)),
                                new PropertyExpression("queueName", new StringLiteralExpression(ManifestsQueueName)),
                                new PropertyExpression("messageCount", new StringLiteralExpression("1"))),
                            // KEDA reads the queue's length as the import identity, with no keys to leak; reading
                            // it needs the Manage claim, hence Data Owner on the queue (D-063).
                            Identity = importIdentityId.AsProvisioningParameter(infrastructure, "import_identity_id"),
                        },
                    },
                },
            };
        };

    /// <summary>The manual backfill and reconcile job (S-033): <c>import --once</c> over the whole container.</summary>
    public static void ConfigureImportAllJob(AzureResourceInfrastructure infrastructure, ContainerAppJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.Name = ImportAllJobName;
        ConfigureImportJobCommon(job);
        job.Configuration.TriggerType = ContainerAppJobTriggerType.Manual;
        job.Configuration.ManualTriggerConfig = new JobConfigurationManualTriggerConfig
        {
            Parallelism = 1,
            ReplicaCompletionCount = 1,
        };
    }

    private static void ConfigureImportJobCommon(ContainerAppJob job)
    {
        job.Location = Region;
        job.Configuration.ReplicaTimeout = ImportTimeoutSeconds;
        // A failed run is not repeated at once: its messages come back by themselves, and the next event or
        // import-all picks up what it left.
        job.Configuration.ReplicaRetryLimit = 0;
        // Decoding embeddings and writing a few thousand rows; billed only while it runs.
        var container = job.Template.Containers.Single().Value
            ?? throw new InvalidOperationException("The import job has no container.");
        container.Resources = new AppContainerResources { Cpu = 1.0, Memory = "2Gi" };
    }

    /// <summary>
    /// <c>geonames update</c> (S-059, D-067): started by hand once after the first deploy and to refresh the
    /// cities, never on a schedule. Its download is a few MB and lands in the replica's temporary storage.
    /// </summary>
    public static void ConfigureGeoNamesJob(AzureResourceInfrastructure infrastructure, ContainerAppJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.Name = GeoNamesJobName;
        job.Location = Region;

        var configuration = job.Configuration;
        configuration.TriggerType = ContainerAppJobTriggerType.Manual;
        configuration.ManualTriggerConfig = new JobConfigurationManualTriggerConfig
        {
            Parallelism = 1,
            ReplicaCompletionCount = 1,
        };
        configuration.ReplicaTimeout = GeoNamesTimeoutSeconds;
        // A refused or failed load keeps the previous cities; repeating it unattended would only fail again.
        configuration.ReplicaRetryLimit = 0;
        // The cities are read whole before the load, so they can be counted (GeoNamesLoadGuard).
        var container = job.Template.Containers.Single().Value
            ?? throw new InvalidOperationException("The GeoNames job has no container.");
        container.Resources = new AppContainerResources { Cpu = 1.0, Memory = "2Gi" };
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
