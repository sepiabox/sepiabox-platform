using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Sepiabox.Ingest.Processing;

/// <summary>
/// What a work folder does with the originals (S-063, D-088). One work folder holds one mode, recorded in its
/// <c>state.db</c> by the first <c>process</c> run, so two preview sizes and two upload rules never mix in one folder.
/// </summary>
public enum WorkFolderMode
{
    /// <summary>Originals are uploaded with everything else. The mode of every work folder made before S-063.</summary>
    KeepsOriginals,

    /// <summary>Originals stay where they are and are never uploaded; previews are larger.</summary>
    IndexOnly,
}

/// <summary>A <c>process</c> run asked for the other mode than the work folder's. A usage error: nothing was read or changed.</summary>
public sealed class WorkFolderModeException : Exception
{
    public WorkFolderModeException()
    {
    }

    public WorkFolderModeException(string message)
        : base(message)
    {
    }

    public WorkFolderModeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public WorkFolderModeException(WorkFolderMode recorded)
        : base($"This work folder {Describe(recorded)}; use a new --out for the other mode.")
    {
    }

    private static string Describe(WorkFolderMode mode) => mode == WorkFolderMode.IndexOnly ? "is index-only" : "keeps originals";
}

/// <summary>Reads and records the mode in the <c>meta</c> table of a work folder's <c>state.db</c>.</summary>
internal static class WorkFolderModes
{
    private const string Key = "mode";

    /// <summary>
    /// Refuses <paramref name="requested"/> when the work folder at <paramref name="databasePath"/> has the other mode,
    /// before anything is read or written: the file is opened read-only. A missing database is a new work folder; a
    /// database with no recorded mode was made before S-063 and keeps originals.
    /// </summary>
    public static void Check(string databasePath, WorkFolderMode requested)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ReadOnlySource(databasePath),
            Pooling = false,
        }.ToString());
        connection.Open();
        var recorded = Read(connection);
        if (recorded != requested)
        {
            throw new WorkFolderModeException(recorded);
        }
    }

    // A refused run must leave the work folder as it was, and even a read-only connection to a WAL database leaves an
    // empty -wal and -shm behind. When no -wal exists, everything committed is in the main file, so it is opened
    // immutable: no locks, no side files. A -wal left by an interrupted run may hold the mode, so then the file is opened
    // normally, read-only, and nothing is missed.
    private static string ReadOnlySource(string databasePath) =>
        File.Exists(databasePath + "-wal")
            ? new Uri(databasePath).AbsoluteUri + "?mode=ro"
            : new Uri(databasePath).AbsoluteUri + "?mode=ro&immutable=1";

    /// <summary>The recorded mode; <see cref="WorkFolderMode.KeepsOriginals"/> when none is, as for a work folder made before S-063.</summary>
    public static WorkFolderMode Read(SqliteConnection connection)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta'";
        if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            return WorkFolderMode.KeepsOriginals;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", Key);
        // A name from a later build reads as keeping originals, the safe side: nothing is left only at its source.
        return command.ExecuteScalar() is string value && value == nameof(WorkFolderMode.IndexOnly)
            ? WorkFolderMode.IndexOnly
            : WorkFolderMode.KeepsOriginals;
    }

    /// <summary>Records the mode unless one is recorded already; a recorded mode is never changed.</summary>
    public static void Record(SqliteConnection connection, WorkFolderMode mode)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT OR IGNORE INTO meta (key, value) VALUES ($key, $value);
            """;
        command.Parameters.AddWithValue("$key", Key);
        command.Parameters.AddWithValue("$value", mode.ToString());
        command.ExecuteNonQuery();
    }
}
