using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sepiabox.Catalog;

namespace Sepiabox.Worker.Tests;

/// <summary>
/// A catalog database set up the way Azure Database for PostgreSQL sets one up: its administrator is not a
/// superuser but has CREATEROLE and CREATEDB and belongs to <c>azure_pg_admin</c>, which owns the database.
/// The container's own user is a superuser, which would let statements pass that Azure refuses.
/// </summary>
internal sealed class AzureLikeCatalog : IAsyncDisposable
{
    public const string AdministratorPassword = "Administrator0123456789";

    private AzureLikeCatalog(string superuser, string administrator, string database)
    {
        Superuser = superuser;
        Administrator = administrator;
        Database = database;
    }

    /// <summary>The container's superuser, on this database.</summary>
    public string Superuser { get; }

    /// <summary>The Azure-like administrator, on this database: what the migrate job connects as.</summary>
    public string Administrator { get; }

    public string Database { get; }

    /// <summary>Creates the administrator and an empty database owned by <c>azure_pg_admin</c>, with the extensions.</summary>
    public static async Task<AzureLikeCatalog> CreateAsync(CatalogFixture fixture)
    {
        // Only for the superuser's connection string; the catalog itself is a database of its own.
        await using var scratch = await fixture.CreateEmptyDatabaseAsync();
        var superuser = fixture.ConnectionStringFor(scratch);
        var suffix = Guid.NewGuid().ToString("N");
        var administrator = "admin_" + suffix;
        var database = "azure_catalog_" + suffix;

        await ExecuteAsync(superuser, $"""
            DO $$ BEGIN
              IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'azure_pg_admin') THEN
                CREATE ROLE azure_pg_admin NOLOGIN;
              END IF;
            END $$;
            CREATE ROLE "{administrator}" WITH LOGIN CREATEROLE CREATEDB NOSUPERUSER PASSWORD '{AdministratorPassword}';
            GRANT azure_pg_admin TO "{administrator}";
            """);
        await ExecuteAsync(superuser, $"CREATE DATABASE \"{database}\" OWNER azure_pg_admin");

        var superuserOnCatalog = On(superuser, database);
        // Azure allows them through azure.extensions; in the container only a superuser can create PostGIS.
        await ExecuteAsync(superuserOnCatalog, "CREATE EXTENSION vector; CREATE EXTENSION postgis;");

        var administratorOnCatalog = new NpgsqlConnectionStringBuilder(superuserOnCatalog)
        {
            Username = administrator,
            Password = AdministratorPassword,
            Pooling = false,
        }.ConnectionString;
        return new AzureLikeCatalog(superuserOnCatalog, administratorOnCatalog, database);
    }

    /// <summary>A context connected as the administrator, as the migrate job is.</summary>
    public CatalogDbContext CreateAdministratorContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, Administrator);
        return new CatalogDbContext(options.Options);
    }

    /// <summary>A connection string on this database for another role.</summary>
    public string As(string role, string password) =>
        new NpgsqlConnectionStringBuilder(Administrator) { Username = role, Password = password }.ConnectionString;

    public ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        return ValueTask.CompletedTask;
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string On(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database, Pooling = false }.ConnectionString;
}
