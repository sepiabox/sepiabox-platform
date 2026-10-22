using System.Globalization;
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
    // One identity per kind of worker job (D-063): migrate reads only the database vault; the import jobs
    // read storage and the queue and never see the administrator's password.
    var migrateIdentity = builder.AddAzureUserAssignedIdentity("migrate-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);
    var importIdentity = builder.AddAzureUserAssignedIdentity("import-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);
    // The faces job writes manifests (S-022): its own identity, so the import jobs stay readers.
    var facesIdentity = builder.AddAzureUserAssignedIdentity("faces-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);
    // The process job writes derivatives, originals and manifests and empties incoming (S-036): its own identity.
    var processIdentity = builder.AddAzureUserAssignedIdentity("process-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);

    // The admin job (S-057, D-108) deletes blobs, which no other identity may: its own, with the write role on storage.
    var adminIdentity = builder.AddAzureUserAssignedIdentity("admin-identity")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureIdentity);

    // The application role's connection, for the web and the import jobs: they read and write rows, never
    // the schema. The server's fixed host name rather than a reference to it: a reference would also grant
    // them the database vault, which holds the administrator's password.
    var appRoleConnection = ReferenceExpression.Create(
        $"Host={AzureInfrastructure.PostgresHostName};Username={AzureInfrastructure.AppRoleName};Password={appRolePassword};Database=catalog");

    // Runs `sepiabox-worker migrate` once per deploy, before the new web revision and import jobs
    // (scripts/deploy.sh). It connects as the administrator, applies the migrations and then creates or
    // updates the application role (S-058). Of the vaults it reads only the database one, so never the
    // Anthropic key, and it needs no storage.
    builder.AddProject<Projects.Sepiabox_Worker>(AzureInfrastructure.MigrateResourceName)
        .WithArgs("migrate")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(catalog)
        .WithEnvironment("Catalog__AppRole__Name", AzureInfrastructure.AppRoleName)
        .WithEnvironment("Catalog__AppRole__Password", appRolePassword)
        .WithAzureUserAssignedIdentity(migrateIdentity)
        .WithRoleAssignments(databaseVault, KeyVaultBuiltInRole.KeyVaultSecretsUser)
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureMigrateJob);

    // Loads GeoNames and names places (S-059, D-067), started by hand. It writes the geonames table, which the
    // application role may only read (D-066), so it connects as the administrator, with migrate's identity: the
    // same single role on the database vault, and nothing else.
    builder.AddProject<Projects.Sepiabox_Worker>(AzureInfrastructure.GeoNamesResourceName)
        .WithArgs("geonames", "update")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(catalog)
        .WithAzureUserAssignedIdentity(migrateIdentity)
        // Migrate's role on the database vault, declared there.
        .WithRoleAssignments(databaseVault, [])
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureGeoNamesJob);

    // Manifests imported when they arrive (S-033, D-063): Event Grid sends the manifests container's
    // BlobCreated events to a Basic Service Bus queue, and an event-driven job imports them. Neither import
    // job ever migrates; both connect as the application role.
    var serviceBus = builder.AddAzureServiceBus("servicebus")
        .ConfigureInfrastructure(AzureInfrastructure.ConfigureServiceBus);
    serviceBus.AddServiceBusQueue("manifests-queue", AzureInfrastructure.ManifestsQueueName);
    serviceBus.AddServiceBusQueue("incoming-queue", AzureInfrastructure.IncomingQueueName);
    builder.AddBicepTemplate("events", "Bicep/events.bicep")
        .WithParameter("storageName", storage.GetOutput("name"))
        .WithParameter("serviceBusName", serviceBus.GetOutput("name"))
        .WithParameter("queueName", AzureInfrastructure.ManifestsQueueName)
        .WithParameter("incomingQueueName", AzureInfrastructure.IncomingQueueName)
        .WithParameter("importPrincipalId", importIdentity.Resource.PrincipalId)
        .WithParameter("processPrincipalId", processIdentity.Resource.PrincipalId);

    // The namespace by host name rather than a reference, which would grant Aspire's default roles on the
    // whole namespace; the import identity's role on the queue alone is in events.bicep.
    builder.AddProject<Projects.Sepiabox_Worker>(AzureInfrastructure.ImportResourceName)
        .WithArgs("import", "--from-queue")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(blobs)
        .WithEnvironment("ConnectionStrings__catalog", appRoleConnection)
        .WithEnvironment("ServiceBus__Namespace", AzureInfrastructure.ServiceBusHostName)
        .WithEnvironment("ServiceBus__Queue", AzureInfrastructure.ManifestsQueueName)
        .WithEnvironment("ServiceBus__MaxDeliveryCount", AzureInfrastructure.ManifestsMaxDeliveryCount.ToString(CultureInfo.InvariantCulture))
        .WithAzureUserAssignedIdentity(importIdentity)
        // The import identity's role on storage, declared once here for both import jobs.
        .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataReader)
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureImportJob(importIdentity.Resource.Id));

    builder.AddProject<Projects.Sepiabox_Worker>(AzureInfrastructure.ImportAllResourceName)
        .WithArgs("import", "--once")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(blobs)
        .WithEnvironment("ConnectionStrings__catalog", appRoleConnection)
        .WithAzureUserAssignedIdentity(importIdentity)
        // The same identity as the import job, whose role on storage is declared there.
        .WithRoleAssignments(storage, [])
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureImportAllJob);

    // Erases faces from the stored manifests after face recognition is turned off (S-022, D-081), every hour. It
    // connects as the application role and writes manifests through ManifestStore; its only storage role, on the
    // manifests container, is in access.bicep.
    builder.AddProject<Projects.Sepiabox_Worker>(AzureInfrastructure.FacesResourceName)
        .WithArgs("faces", "sweep")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(blobs)
        .WithEnvironment("ConnectionStrings__catalog", appRoleConnection)
        .WithAzureUserAssignedIdentity(facesIdentity)
        .WithRoleAssignments(storage, [])
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureFacesJob);

    // Files that arrive in `incoming` from a cloud source (S-036, D-107): processed by the worker from the Dockerfile that
    // gives it FFmpeg and the models, an event-driven job like the import. Its storage roles are on the four containers
    // it touches, in access.bicep; it connects to the catalog as the application role (it only reads the face setting).
    builder.AddDockerfile(AzureInfrastructure.ProcessResourceName, "../..", "infra/worker/Dockerfile")
        .WithArgs("process", "--from-queue")
        .WithReference(blobs)
        .WithEnvironment("ConnectionStrings__catalog", appRoleConnection)
        .WithEnvironment("ServiceBus__Namespace", AzureInfrastructure.ServiceBusHostName)
        .WithEnvironment("ServiceBus__Queue", AzureInfrastructure.IncomingQueueName)
        .WithEnvironment("ServiceBus__MaxDeliveryCount", AzureInfrastructure.IncomingMaxDeliveryCount.ToString(CultureInfo.InvariantCulture))
        // Azure has the Cold tier; Azurite does not (D-004).
        .WithEnvironment("Processing__OriginalsCold", "true")
        .WithAzureUserAssignedIdentity(processIdentity)
        .WithRoleAssignments(storage, [])
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureProcessJob(processIdentity.Resource.Id));

    // Administrative commands, started by hand (S-057, D-108): `assets delete --source <name>`. The arguments here only
    // make a run without any fail with a usage message; the real ones are given when the job is started. It connects as the
    // application role, which may delete rows but never change the schema. Its identity writes only the manifests, derivatives
    // and originals containers (access.bicep); an account-wide write role would also reach the web's key ring (D-062) and
    // the incoming container.
    builder.AddProject<Projects.Sepiabox_Worker>(AzureInfrastructure.AdminResourceName)
        .WithArgs("assets", "delete")
        .WithReferenceEnvironment(ConnectionStringsOnly)
        .WithReference(blobs)
        .WithEnvironment("ConnectionStrings__catalog", appRoleConnection)
        .WithAzureUserAssignedIdentity(adminIdentity)
        .WithRoleAssignments(storage, [])
        .PublishAsAzureContainerAppJob(AzureInfrastructure.ConfigureAdminJob);

    // The web's custom domain (S-035, D-065), empty for none. Set after the DNS records exist, first with the
    // binding Disabled (the host name is added), then SniEnabled (domain.bicep creates the managed certificate
    // and the web binds it); docs/operations.md has the steps. No published default: azd would then read only
    // AZURE_CUSTOM_DOMAIN and ignore the environment's infra.parameters (D-072); scripts/deploy.sh and the Deploy
    // workflow always set these, empty and Disabled for none.
    var customDomain = builder.AddParameter(AzureInfrastructure.CustomDomainParameter);
    var customDomainBinding = builder.AddParameter(AzureInfrastructure.CustomDomainBindingParameter);
    builder.AddBicepTemplate("domain", "Bicep/domain.bicep")
        .WithParameter("environmentName", environment.GetOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_NAME"))
        .WithParameter("customDomain", customDomain)
        .WithParameter("customDomainBinding", customDomainBinding)
        .WithParameter("certificateName", AzureInfrastructure.CustomDomainCertificateName);

    // What the role helpers cannot scope: the key ring's container and key, and the uploaders (D-062).
    // Like the custom domain, without a published default (D-072).
    var uploaders = builder.AddParameter(AzureInfrastructure.UploaderPrincipalIdsParameter);
    var access = builder.AddBicepTemplate("access", "Bicep/access.bicep")
        .WithParameter("storageName", storage.GetOutput("name"))
        .WithParameter("vaultName", vault.GetOutput("name"))
        .WithParameter("webPrincipalId", webIdentity.Resource.PrincipalId)
        .WithParameter("facesPrincipalId", facesIdentity.Resource.PrincipalId)
        .WithParameter("processPrincipalId", processIdentity.Resource.PrincipalId)
        .WithParameter("adminPrincipalId", adminIdentity.Resource.PrincipalId)
        .WithParameter("uploaderPrincipalIds", uploaders);

    web.WithReference(insights)
        .WithAzureUserAssignedIdentity(webIdentity)
        // Reads blobs and signs SAS links with a user delegation key (D-039); writes only the key ring.
        .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataReader)
        // The owner's secrets: sign-in, the access list and the Anthropic key, read as configuration.
        .WithReference(vault)
        .WithRoleAssignments(vault, KeyVaultBuiltInRole.KeyVaultSecretsUser)
        .WithEnvironment("ConnectionStrings__catalog", appRoleConnection)
        .WithEnvironment("DataProtection__KeyUri", access.GetOutput("dataProtectionKeyUri"))
        .WithEnvironment("Search__TimeZone", AzureInfrastructure.SearchTimeZone)
        // The CLIP text model in the web's image (S-056): its base image sepiabox-web-models, built by scripts/web-models.sh.
        .WithEnvironment("Search__ModelFolder", AzureInfrastructure.WebModelFolder)
        // Only the Container Apps default host and the custom domain, if any, reach the app (D-048, D-065);
        // an empty custom domain leaves an empty entry, which host filtering ignores.
        .WithEnvironment("AllowedHosts", ReferenceExpression.Create(
            $"{AzureInfrastructure.WebAppName}.{environment.GetOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN")};{customDomain}"))
        .PublishAsAzureContainerApp(AzureInfrastructure.ConfigureWebApp(
            customDomain, customDomainBinding, environment.GetOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_ID")));
}

builder.Build().Run();
