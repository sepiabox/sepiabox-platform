using Microsoft.Data.Sqlite;

namespace Sepiabox.Ingest.Processing;

public enum FileStatus
{
    Done,
    Failed,

    /// <summary>A video described without its poster and 720p version because FFmpeg was not available.</summary>
    Pending,
}

/// <summary>What <c>process</c> last saw of a source file. <c>ModifiedTicks</c> is the last write time in UTC ticks.</summary>
public sealed record FileState(string Path, long Size, long ModifiedTicks, string? Sha256, FileStatus Status);

/// <summary>
/// Remembers which files were already processed, so an interrupted or repeated run only reads
/// what is new or changed. One SQLite file per work folder.
/// </summary>
public sealed class StateStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public StateStore(string databasePath)
    {
        // No pooling: the file must be released when the store is disposed.
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        _connection.Open();

        Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS files (
                path TEXT PRIMARY KEY,
                size INTEGER NOT NULL,
                modified_ticks INTEGER NOT NULL,
                sha256 TEXT,
                status TEXT NOT NULL
            );
            """);
    }

    public FileState? Get(string path)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT size, modified_ticks, sha256, status FROM files WHERE path = $path";
        command.Parameters.AddWithValue("$path", path);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new FileState(
            path,
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            Enum.Parse<FileStatus>(reader.GetString(3)));
    }

    public void Save(FileState state)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO files (path, size, modified_ticks, sha256, status)
            VALUES ($path, $size, $modified, $sha256, $status)
            ON CONFLICT (path) DO UPDATE SET
                size = excluded.size, modified_ticks = excluded.modified_ticks,
                sha256 = excluded.sha256, status = excluded.status
            """;
        command.Parameters.AddWithValue("$path", state.Path);
        command.Parameters.AddWithValue("$size", state.Size);
        command.Parameters.AddWithValue("$modified", state.ModifiedTicks);
        command.Parameters.AddWithValue("$sha256", (object?)state.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", state.Status.ToString());
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
