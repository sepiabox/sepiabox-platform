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
    // Any constant shared by every migrate run: two runs at once (a deploy retried while one runs) would both
    // find no role and both try to create it.
    private const long AdvisoryLockKey = 0x5E91AB0C;

    // Tables the role may read but never write: the migration history, PostGIS's reference table and EF's
    // migration lock table, if the provider has one.
    private static readonly string[] s_readOnlyTables = ["__EFMigrationsHistory", "__EFMigrationsLock", "spatial_ref_sys"];

    /// <summary>
    /// Creates the role, or updates its password, and grants it the catalog's rows. Run by the administrator
    /// after the migrations on every deploy, so tables added by a migration are granted too.
    /// </summary>
    /// <remarks>
    /// Written for Azure's administrator, which is not a superuser: on PostgreSQL 16 and later only a superuser
    /// may even restate <c>NOSUPERUSER</c>, so the attributes are set when the role is created and an update
    /// sets only the login and the password. <c>CREATE ROLE</c> takes no parameters, so the name (validated to
    /// characters that need no quoting) and the password are written into the statements; the password only
    /// as a SCRAM-SHA-256 verifier, so neither the statement nor the server's error context holds it. A failure
    /// is reported with its SQL state and message only, never the statement. Connecting to the database and
    /// using the <c>public</c> schema are left to PUBLIC's default privileges: on Azure the administrator owns
    /// the tables the migrations create, but not necessarily the database or the schema.
    /// </remarks>
    public static async Task EnsureAsync(
        NpgsqlConnection connection, string name, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Validate(name, password);
        var verifier = ScramVerifier.Create(password);
        var readOnlyTables = string.Join(", ", s_readOnlyTables.Select(table => $"'{table}'"));

        var sql = $"""
            SELECT pg_advisory_xact_lock({AdvisoryLockKey});
            DO $$
            DECLARE
              read_only text;
            BEGIN
              IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{name}') THEN
                ALTER ROLE "{name}" WITH LOGIN PASSWORD '{verifier}';
              ELSE
                CREATE ROLE "{name}" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{verifier}';
              END IF;
              GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO "{name}";
              GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO "{name}";
              FOR read_only IN
                SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename IN ({readOnlyTables})
              LOOP
                EXECUTE format('REVOKE INSERT, UPDATE, DELETE ON public.%I FROM %I', read_only, '{name}');
              END LOOP;
            END
            $$;
            """;

        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            // One command, so the advisory lock is held until the role and its grants are done.
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception)
        {
            // The exception's Where, InternalQuery, Statement and Data can quote the statement; keep none of them.
            throw new InvalidOperationException(
                $"Could not create or update the application role {name}: {exception.SqlState}: {exception.MessageText}");
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
