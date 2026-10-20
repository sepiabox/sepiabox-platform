using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sepiabox.Catalog;

namespace Sepiabox.Worker.Tests;

/// <summary>The application role the web connects with in Azure (S-058, D-062), on a real PostgreSQL.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogAppRoleTests(CatalogFixture fixture)
{
    private const string Password = "Abcdefghijklmnop0123456789";

    // Roles belong to the server, not to a database, so each test has its own.
    private readonly string _role = "app_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task App_role_reads_and_writes_the_catalog_rows_when_migrate_created_it()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await CatalogAppRole.EnsureAsync((NpgsqlConnection)db.Database.GetDbConnection(), _role, Password, default);

        await using var app = await ConnectAsAppAsync(db);

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
    }

    [Theory]
    [InlineData("CREATE TABLE intruder (id int)")]
    [InlineData("ALTER TABLE assets ADD COLUMN intruder int")]
    [InlineData("DROP TABLE manifest_imports")]
    [InlineData("TRUNCATE assets")]
    [InlineData("CREATE INDEX intruder ON assets (id)")]
    public async Task App_role_cannot_change_the_schema(string statement)
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await CatalogAppRole.EnsureAsync((NpgsqlConnection)db.Database.GetDbConnection(), _role, Password, default);
        await using var app = await ConnectAsAppAsync(db);

        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(app, statement));

        // 42501 insufficient privilege; "must be owner" is the same code.
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Ensuring_the_role_again_keeps_it_and_takes_the_new_password()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await CatalogAppRole.EnsureAsync(connection, _role, Password, default);

        await CatalogAppRole.EnsureAsync(connection, _role, "Zyxwvutsrqponmlk9876543210", default);

        await using var app = await ConnectAsAppAsync(db, "Zyxwvutsrqponmlk9876543210");
        Assert.Equal(0L, await ScalarAsync(app, "SELECT count(*) FROM manifest_imports"));
        await Assert.ThrowsAsync<PostgresException>(() => ConnectAsAppAsync(db, Password));
    }

    private async Task<NpgsqlConnection> ConnectAsAppAsync(CatalogDbContext db, string password = Password)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionStringFor(db))
        {
            Username = _role,
            Password = password,
            Pooling = false,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
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
