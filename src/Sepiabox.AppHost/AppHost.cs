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

// Runs once at startup: applies migrations, imports whatever manifests are in storage, and exits.
// Automatic import when a manifest arrives comes with S-033.
builder.AddProject<Projects.Sepiabox_Worker>("worker")
    .WithArgs("import", "--once", "--migrate")
    .WithReference(catalog)
    .WaitFor(catalog)
    .WithReference(blobs)
    .WaitFor(blobs);

builder.AddProject<Projects.Sepiabox_Web>("web")
    .WithReference(catalog)
    .WaitFor(catalog)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithExternalHttpEndpoints();

builder.Build().Run();
