using Azure.Storage.Blobs;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sepiabox.Catalog;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;

namespace Sepiabox.Tests.Catalog;

/// <summary>
/// One PostgreSQL (built from <c>infra/postgres</c>, D-012) and one Azurite for the whole collection;
/// every test gets its own database and blob container, so tests never see each other's data.
/// </summary>
public sealed class CatalogFixture : IAsyncLifetime
{
    // Lets a developer reuse an image they already have instead of building infra/postgres; CI never sets it.
    private const string ImageOverrideVariable = "SEPIABOX_TEST_POSTGRES_IMAGE";

    private PostgreSqlContainer _postgres = null!;

    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        // The storage SDK speaks a newer API version than Azurite announces; Aspire's emulator does the same.
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public BlobServiceClient Blobs { get; private set; } = null!;

    public string BlobConnectionString => _azurite.GetConnectionString();

    public async Task InitializeAsync()
    {
        var image = Environment.GetEnvironmentVariable(ImageOverrideVariable);
        if (string.IsNullOrEmpty(image))
        {
            var built = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(CommonDirectoryPath.GetGitDirectory(), "infra/postgres")
                .WithName("sepiabox-postgres-tests:latest")
                .Build();
            await built.CreateAsync();
            image = built.FullName;
        }

        _postgres = new PostgreSqlBuilder(image).Build();
        await Task.WhenAll(_postgres.StartAsync(), _azurite.StartAsync());
        Blobs = new BlobServiceClient(BlobConnectionString);
    }

    /// <summary>Creates an empty database and returns a context for it, without applying migrations.</summary>
    public async Task<CatalogDbContext> CreateEmptyDatabaseAsync()
    {
        var name = "catalog_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = ConnectionStringFor(name);
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, connectionString);
        return new CatalogDbContext(options.Options);
    }

    /// <summary>
    /// The full connection string (with the password, which an opened context no longer reports) for a
    /// database created by this fixture, for code under test that opens its own connections.
    /// </summary>
    public string ConnectionStringFor(CatalogDbContext db) => ConnectionStringFor(db.Database.GetDbConnection().Database);

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public async Task<CatalogDbContext> CreateMigratedDatabaseAsync()
    {
        var db = await CreateEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    public async Task<BlobContainerClient> CreateContainerAsync()
    {
        var container = Blobs.GetBlobContainerClient("manifests-" + Guid.NewGuid().ToString("N"));
        await container.CreateAsync();
        return container;
    }

    public async Task DisposeAsync()
    {
        await _azurite.DisposeAsync();
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogGroup : ICollectionFixture<CatalogFixture>
{
    public const string Name = "Catalog";
}
