using System.Globalization;
using Microsoft.Data.Sqlite;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Processing;

public enum FileStatus
{
    Done,
    Failed,

    /// <summary>A video described without its poster and 720p version because FFmpeg was not available.</summary>
    Pending,

    /// <summary>A file with a video extension that holds no video, such as a tracker-music <c>.mod</c> (D-045).</summary>
    NotVideo,
}

/// <summary>What <c>process</c> last saw of a source file.</summary>
/// <param name="Path">The source file.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="ModifiedTicks">Its last write time in UTC ticks.</param>
/// <param name="Sha256">Its content hash, once read.</param>
/// <param name="Status">What became of it.</param>
/// <param name="Reason">Why it was left out, for <see cref="FileStatus.NotVideo"/>; null otherwise, or when not recorded.</param>
public sealed record FileState(string Path, long Size, long ModifiedTicks, string? Sha256, FileStatus Status, NoVideoReason? Reason = null);

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
                status TEXT NOT NULL,
                reason TEXT
            );
            """);

        // Work folders made before the left-out reason was stored gain the column; their rows read as no reason.
        using var hasReason = _connection.CreateCommand();
        hasReason.CommandText = "SELECT COUNT(*) FROM pragma_table_info('files') WHERE name = 'reason'";
        if (Convert.ToInt64(hasReason.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            Execute("ALTER TABLE files ADD COLUMN reason TEXT");
        }
    }

    public FileState? Get(string path)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT size, modified_ticks, sha256, status, reason FROM files WHERE path = $path";
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
            Enum.Parse<FileStatus>(reader.GetString(3)),
            reader.IsDBNull(4) ? null : ParseReason(reader.GetString(4)));
    }

    public void Save(FileState state)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO files (path, size, modified_ticks, sha256, status, reason)
            VALUES ($path, $size, $modified, $sha256, $status, $reason)
            ON CONFLICT (path) DO UPDATE SET
                size = excluded.size, modified_ticks = excluded.modified_ticks,
                sha256 = excluded.sha256, status = excluded.status, reason = excluded.reason
            """;
        command.Parameters.AddWithValue("$path", state.Path);
        command.Parameters.AddWithValue("$size", state.Size);
        command.Parameters.AddWithValue("$modified", state.ModifiedTicks);
        command.Parameters.AddWithValue("$sha256", (object?)state.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", state.Status.ToString());
        command.Parameters.AddWithValue("$reason", (object?)state.Reason?.ToString() ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    // Only a known reason's name; a number, Unknown or a name from a later build reads as no reason, so the file
    // is read again.
    private static NoVideoReason? ParseReason(string value) =>
        Enum.GetNames<NoVideoReason>().Contains(value, StringComparer.Ordinal)
        && Enum.Parse<NoVideoReason>(value) is var reason and not NoVideoReason.Unknown
            ? reason
            : null;

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
