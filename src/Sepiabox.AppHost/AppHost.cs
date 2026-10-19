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

var web = builder.AddProject<Projects.Sepiabox_Web>("web")
    .WithReference(catalog)
    .WaitFor(catalog)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithExternalHttpEndpoints();

// Local runs only (D-034): published, a project becomes an always-on Container App, and the cloud
// gets its own job in S-033.
if (builder.ExecutionContext.IsRunMode)
{
    var migrate = builder.AddProject<Projects.Sepiabox_Worker>("worker-migrate")
        .WithArgs("migrate")
        .WithReference(catalog)
        .WaitFor(catalog);

    // Imports whatever manifests are in storage, then exits; it exits with 1 when a manifest has a
    // problem, which must not keep the web from starting.
    builder.AddProject<Projects.Sepiabox_Worker>("worker-import")
        .WithArgs("import", "--once")
        .WithReference(catalog)
        .WithReference(blobs)
        .WaitFor(blobs)
        .WaitForCompletion(migrate);

    web.WaitForCompletion(migrate);
}

builder.Build().Run();
