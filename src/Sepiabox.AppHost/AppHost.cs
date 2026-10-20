using Aspire.Hosting.Azure;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.Storage;
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

    // Locally the web connects as the container's administrator; in Azure as the application role below.
    web.WithReference(catalog)
        .WaitForCompletion(migrate);
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

    // The catalog code reads a plain Npgsql connection string, so the server takes a password. Declared
    // rather than left to Aspire so the password has no character that needs quoting (D-061); azd generates
    // them once and keeps them in the azd environment (docs/operations.md). Aspire keeps the administrator's
    // connection strings in a vault of their own that only the migrate job reads, so the web never holds the
    // administrator's password; the web connects as the application role (D-062).
    var postgresLogin = builder.AddParameter("pg-username", AzureInfrastructure.PostgresLogin, secret: false);
    var postgresPassword = builder.AddParameter("pg-password", AzureInfrastructure.PostgresPassword, secret: true);
    var appRolePassword = builder.AddParameter("pg-app-password", AzureInfrastructure.PostgresPassword, secret: true);
    var databaseVault = builder.AddAzureKeyVault("database-vault")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureKeyVault);
    postgres.WithPasswordAuthentication(databaseVault, postgresLogin, postgresPassword)
        .ConfigureInfrastructure(AzureInfrastructure.ConfigurePostgres);
    storage.ConfigureInfrastructure(AzureInfrastructure.ConfigureStorage);

    // Declared rather than left to Aspire so they too are pinned to the region.
    var webIdentity = builder.AddAzureUserAssignedIdentity("web-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);
    var migrateIdentity = builder.AddAzureUserAssignedIdentity("migrate-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);

    // Runs `sepiabox-worker migrate` once per deploy, before the new web revision (scripts/deploy.sh).
    // It connects as the administrator, applies the migrations and then creates or updates the web's
    // application role (S-058). Read access to storage is for the import and GeoNames jobs that share its
    // identity later (S-033, S-059). Of the vaults it reads only the database one, so never the Anthropic key.
    builder.AddProject<Projects.Sepiabox_Worker>(AzureInfrastructure.MigrateResourceName)
        .WithArgs("migrate")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(catalog)
        .WithEnvironment("Catalog__AppRole__Name", AzureInfrastructure.AppRoleName)
        .WithEnvironment("Catalog__AppRole__Password", appRolePassword)
        .WithAzureUserAssignedIdentity(migrateIdentity)
        .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataReader)
        .WithRoleAssignments(databaseVault, KeyVaultBuiltInRole.KeyVaultSecretsUser)
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureMigrateJob);

    // What the role helpers cannot scope: the key ring's container and key, and the uploaders (D-062).
    var uploaders = builder.AddParameter(AzureInfrastructure.UploaderPrincipalIdsParameter, "", publishValueAsDefault: true);
    var access = builder.AddBicepTemplate("access", "Bicep/access.bicep")
        .WithParameter("storageName", storage.GetOutput("name"))
        .WithParameter("vaultName", vault.GetOutput("name"))
        .WithParameter("webPrincipalId", webIdentity.Resource.PrincipalId)
        .WithParameter("uploaderPrincipalIds", uploaders);

    web.WithReference(insights)
        .WithAzureUserAssignedIdentity(webIdentity)
        // Reads blobs and signs SAS links with a user delegation key (D-039); writes only the key ring.
        .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataReader)
        // The owner's secrets: sign-in, the access list and the Anthropic key, read as configuration.
        .WithReference(vault)
        .WithRoleAssignments(vault, KeyVaultBuiltInRole.KeyVaultSecretsUser)
        .WithEnvironment("ConnectionStrings__catalog", ReferenceExpression.Create(
            // The server's fixed host name rather than a reference to it: a reference would also grant the web
            // the database vault, which holds the administrator's password.
            $"Host={AzureInfrastructure.PostgresHostName};Username={AzureInfrastructure.AppRoleName};Password={appRolePassword};Database=catalog"))
        .WithEnvironment("DataProtection__KeyUri", access.GetOutput("dataProtectionKeyUri"))
        // Only the Container Apps default host reaches the app (D-048); S-035 adds the custom domain.
        .WithEnvironment("AllowedHosts", ReferenceExpression.Create(
            $"{AzureInfrastructure.WebAppName}.{environment.GetOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN")}"))
        .PublishAsAzureContainerApp(AzureInfrastructure.ConfigureWebApp);
}

builder.Build().Run();
