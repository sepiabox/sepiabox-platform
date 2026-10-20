using Aspire.Hosting.Azure;
using Sepiabox.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// Azure Database for PostgreSQL Flexible Server in the cloud; locally, a container built from
// infra/postgres so pgvector and PostGIS are available exactly as in Azure.
var postgres = builder.AddAzurePostgresFlexibleServer("pg")
    .RunAsContainer(container => container
        .WithDockerfile("../../infra/postgres")
        .WithDataVolume());
var catalog = postgres.AddDatabase("catalog");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var blobs = storage.AddBlobs("blobs");

// The apps read only ConnectionStrings:*, so the separate host, user and password variables are left out.
const ReferenceEnvironmentInjectionFlags ConnectionStringsOnly = ReferenceEnvironmentInjectionFlags.ConnectionString;

var web = builder.AddProject<Projects.Sepiabox_Web>("web")
    .WithReferenceEnvironment(ConnectionStringsOnly)
    .WithReference(catalog)
    .WaitFor(catalog)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithExternalHttpEndpoints();

if (builder.ExecutionContext.IsRunMode)
{
    // Local runs only (D-034): published, a project becomes an always-on Container App, so the cloud
    // has the migrate job below instead, and S-033 adds the import job.
    var migrate = builder.AddProject<Projects.Sepiabox_Worker>("worker-migrate")
        .WithArgs("migrate")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(catalog)
        .WaitFor(catalog);

    // Imports whatever manifests are in storage, then exits; it exits with 1 when a manifest has a
    // problem, which must not keep the web from starting.
    builder.AddProject<Projects.Sepiabox_Worker>("worker-import")
        .WithArgs("import", "--once")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(catalog)
        .WithReference(blobs)
        .WaitFor(blobs)
        .WaitForCompletion(migrate);

    web.WaitForCompletion(migrate);
}
else
{
    // The cloud-only resources are added only when publishing, so a local run never provisions anything
    // in Azure.
    var vault = builder.AddAzureKeyVault("vault")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureKeyVault);
    var logs = builder.AddAzureLogAnalyticsWorkspace("logs")
        .PublishAsExisting(AzureInfrastructure.LogAnalyticsWorkspaceName, resourceGroup: null);
    var insights = builder.AddAzureApplicationInsights("insights", logs)
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureApplicationInsights);
    var registry = builder.AddAzureContainerRegistry("registry")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureContainerRegistry);
    var environment = builder.AddAzureContainerAppEnvironment("aca")
        .WithAzureLogAnalyticsWorkspace(logs)
        .WithAzureContainerRegistry(registry)
        // Telemetry goes to Application Insights; the managed dashboard would be one more thing billed.
        .WithDashboard(false)
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureContainerAppEnvironment);

    // The catalog code reads a plain Npgsql connection string, so the server takes a password; Aspire
    // keeps the connection strings in Key Vault and the apps read them as Key Vault references.
    postgres.WithPasswordAuthentication(vault)
        .ConfigureInfrastructure(AzureInfrastructure.ConfigurePostgres);
    storage.ConfigureInfrastructure(AzureInfrastructure.ConfigureStorage);

    // Declared rather than left to Aspire so they too are pinned to the region; S-058 sets their roles.
    var webIdentity = builder.AddAzureUserAssignedIdentity("web-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);
    var migrateIdentity = builder.AddAzureUserAssignedIdentity("migrate-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);

    // Runs `sepiabox-worker migrate` once per deploy, before the new web revision (scripts/deploy.sh).
    builder.AddProject<Projects.Sepiabox_Worker>(AzureInfrastructure.MigrateResourceName)
        .WithArgs("migrate")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(catalog)
        .WithAzureUserAssignedIdentity(migrateIdentity)
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureMigrateJob);

    web.WithReference(insights)
        .WithAzureUserAssignedIdentity(webIdentity)
        // Only the Container Apps default host reaches the app (D-048); S-035 adds the custom domain.
        .WithEnvironment("AllowedHosts", ReferenceExpression.Create(
            $"{AzureInfrastructure.WebAppName}.{environment.GetOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN")}"))
        .PublishAsAzureContainerApp(AzureInfrastructure.ConfigureWebApp);
}

builder.Build().Run();
