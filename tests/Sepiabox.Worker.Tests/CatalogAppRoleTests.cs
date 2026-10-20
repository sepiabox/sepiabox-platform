using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sepiabox.Catalog;

namespace Sepiabox.Worker.Tests;

/// <summary>
/// The application role the web connects with in Azure (S-058, D-062), created by an Azure-like
/// administrator (not a superuser) on a real PostgreSQL, as <c>sepiabox-worker migrate</c> does on every deploy.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogAppRoleTests(CatalogFixture fixture)
{
    private const string Password = "Abcdefghijklmnop0123456789";
    private const string NewPassword = "Zyxwvutsrqponmlk9876543210";

    // Roles belong to the server, not to a database, so each test has its own.
    private readonly string _role = "app_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task App_role_reads_and_writes_the_catalog_rows_when_migrate_created_it()
    {
        await using var catalog = await MigratedAsync();
        await EnsureAsync(catalog, Password);

        await using var app = await ConnectAsync(catalog, Password);

        Assert.Equal(0L, await ScalarAsync(app, "SELECT count(*) FROM assets"));
        foreach (var table in new[] { "assets", "asset_sources", "derivatives", "asset_embeddings", "manifest_imports" })
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                Assert.True(
                    (bool)(await ScalarAsync(app, $"SELECT has_table_privilege('{table}', '{privilege}')"))!,
                    $"{_role} lacks {privilege} on {table}.");
            }
        }

        // The import jobs name places with the GeoNames cities (S-012), which only the administrator loads (D-066).
        Assert.True((bool)(await ScalarAsync(app, "SELECT has_table_privilege('geonames', 'SELECT')"))!);
    }

    [Fact]
    public async Task Ensuring_the_role_on_every_deploy_keeps_it_and_takes_the_new_password()
    {
        await using var catalog = await MigratedAsync();
        await EnsureAsync(catalog, Password);

        // The second deploy: the role exists, and a non-superuser administrator updates it.
        await EnsureAsync(catalog, NewPassword);

        await using var app = await ConnectAsync(catalog, NewPassword);
        Assert.Equal(0L, await ScalarAsync(app, "SELECT count(*) FROM manifest_imports"));
        await Assert.ThrowsAsync<PostgresException>(() => ConnectAsync(catalog, Password));
    }

    [Fact]
    public async Task App_role_has_no_rights_beyond_logging_in()
    {
        await using var catalog = await MigratedAsync();
        await EnsureAsync(catalog, Password);
        await using var app = await ConnectAsync(catalog, Password);

        var role = await ScalarAsync(
            app,
            "SELECT format('%s %s %s %s %s', rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolcanlogin) FROM pg_roles WHERE rolname = current_user");

        Assert.Equal("f f f f t", role);
    }

    [Theory]
    [InlineData("CREATE TABLE intruder (id int)")]
    [InlineData("ALTER TABLE assets ADD COLUMN intruder int")]
    [InlineData("DROP TABLE manifest_imports")]
    [InlineData("TRUNCATE assets")]
    [InlineData("CREATE INDEX intruder ON assets (id)")]
    [InlineData("DELETE FROM \"__EFMigrationsHistory\"")]
    [InlineData("INSERT INTO \"__EFMigrationsHistory\" VALUES ('intruder', '0')")]
    [InlineData("UPDATE spatial_ref_sys SET srtext = '' WHERE srid = 4326")]
    [InlineData("DELETE FROM spatial_ref_sys")]
    [InlineData("INSERT INTO geonames (geoname_id, name, ascii_name, country_code, population, location) VALUES (1, 'x', 'x', 'PT', 0, 'POINT(0 0)')")]
    [InlineData("UPDATE geonames SET name = 'x'")]
    [InlineData("DELETE FROM geonames")]
    [InlineData("TRUNCATE geonames")]
    public async Task App_role_cannot_change_the_schema_or_the_migration_history(string statement)
    {
        await using var catalog = await MigratedAsync();
        await EnsureAsync(catalog, Password);
        await using var app = await ConnectAsync(catalog, Password);

        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(app, statement));

        // 42501 insufficient privilege; "must be owner" is the same code.
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Postgres_takes_the_scram_verifier_as_it_is_and_the_role_logs_in_with_the_password()
    {
        await using var catalog = await MigratedAsync();
        await EnsureAsync(catalog, Password);
        var verifier = ScramVerifier.Create(NewPassword);

        // A string PostgreSQL did not recognise as a verifier would be hashed again, with a salt of its own.
        await AzureLikeCatalog.ExecuteAsync(catalog.Administrator, $"ALTER ROLE \"{_role}\" WITH PASSWORD '{verifier}'");
        var stored = await ScalarOnAsync(catalog.Superuser, $"SELECT rolpassword FROM pg_authid WHERE rolname = '{_role}'");

        Assert.Equal(verifier, stored);
        await using var app = await ConnectAsync(catalog, NewPassword);
    }

    [Fact]
    public async Task A_failure_reports_only_the_error_without_the_statement()
    {
        await using var catalog = await MigratedAsync();
        // An administrator that may not create roles, so the statement fails on the server.
        var weak = "weak_" + Guid.NewGuid().ToString("N");
        await AzureLikeCatalog.ExecuteAsync(
            catalog.Superuser, $"CREATE ROLE \"{weak}\" WITH LOGIN NOSUPERUSER NOCREATEROLE PASSWORD 'Weak0123456789abcdef'");
        await using var connection = new NpgsqlConnection(catalog.As(weak, "Weak0123456789abcdef"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CatalogAppRole.EnsureAsync(connection, _role, Password, default));

        Assert.Null(error.InnerException);
        Assert.Empty(error.Data);
        Assert.Contains(PostgresErrorCodes.InsufficientPrivilege, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SCRAM-SHA-256", error.ToString(), StringComparison.Ordinal);
    }

    private static async Task<AzureLikeCatalog> MigratedAsync(CatalogFixture fixture)
    {
        var catalog = await AzureLikeCatalog.CreateAsync(fixture);
        await using var db = catalog.CreateAdministratorContext();
        await db.Database.MigrateAsync();
        return catalog;
    }

    private Task<AzureLikeCatalog> MigratedAsync() => MigratedAsync(fixture);

    private async Task EnsureAsync(AzureLikeCatalog catalog, string password)
    {
        await using var connection = new NpgsqlConnection(catalog.Administrator);
        await CatalogAppRole.EnsureAsync(connection, _role, password, default);
    }

    private async Task<NpgsqlConnection> ConnectAsync(AzureLikeCatalog catalog, string password)
    {
        var connection = new NpgsqlConnection(catalog.As(_role, password));
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<object?> ScalarOnAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await ScalarAsync(connection, sql);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
