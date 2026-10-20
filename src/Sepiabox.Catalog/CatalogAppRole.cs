using System.Text.RegularExpressions;
using Npgsql;

namespace Sepiabox.Catalog;

/// <summary>
/// The least-privilege PostgreSQL role the web connects with in Azure (S-058, D-062): it reads and writes
/// the catalog's rows but cannot change the schema, which only the migrations (run as the server's
/// administrator) do.
/// </summary>
public static partial class CatalogAppRole
{
    /// <summary>
    /// Creates the role, or updates its password, and grants it the catalog's rows. Run by the administrator
    /// after the migrations on every deploy, so tables added by a migration are granted too.
    /// </summary>
    /// <remarks>
    /// <c>CREATE ROLE</c> and <c>GRANT</c> take no parameters, so the name and password are written into the
    /// statements; both are validated to characters that need no quoting. The statements run on the
    /// connection directly, not through EF, so the password never reaches EF's command log. Connecting to
    /// the database and using the <c>public</c> schema are left to PUBLIC's default privileges: on Azure the
    /// administrator owns the tables the migrations create, but not necessarily the database or the schema.
    /// </remarks>
    public static async Task EnsureAsync(
        NpgsqlConnection connection, string name, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Validate(name, password);

        var sql = $"""
            DO $$
            BEGIN
              IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{name}') THEN
                ALTER ROLE "{name}" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{password}';
              ELSE
                CREATE ROLE "{name}" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{password}';
              END IF;
            END
            $$;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO "{name}";
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO "{name}";
            """;

        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>Throws when the name or password could not be written into a statement as they are.</summary>
    public static void Validate(string name, string password)
    {
        if (name is null || !RoleName().IsMatch(name))
        {
            throw new ArgumentException(
                "The application role's name must be 1 to 63 lower case letters, digits or '_', starting with a letter.",
                nameof(name));
        }

        if (password is null || !Password().IsMatch(password))
        {
            throw new ArgumentException(
                "The application role's password must be 16 to 128 letters or digits.", nameof(password));
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,62}\z")]
    private static partial Regex RoleName();

    [GeneratedRegex(@"^[A-Za-z0-9]{16,128}\z")]
    private static partial Regex Password();
}
