using System.Globalization;
using Microsoft.Data.Sqlite;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Processing;

public enum FileStatus
{
    Done,
    Failed,

    /// <summary>A video described without its poster and 720p version because FFmpeg was not available.</summary>
    Pending,

    /// <summary>A file with a video extension that holds no video, such as a tracker-music <c>.mod</c> (D-045).</summary>
    NotVideo,

    /// <summary>
    /// A photo whose content cannot be decoded (a truncated or corrupt file, D-105): described, without thumbnails, and
    /// not read again while its size and modification time stay the same, unless <c>--retry-left-out</c> asks.
    /// </summary>
    Damaged,
}

/// <summary>What <c>process</c> last saw of a source file.</summary>
/// <param name="Path">The source file.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="ModifiedTicks">Its last write time in UTC ticks.</param>
/// <param name="Sha256">Its content hash, once read.</param>
/// <param name="Status">What became of it.</param>
/// <param name="Reason">Why it was left out, for <see cref="FileStatus.NotVideo"/>; null otherwise, or when not recorded.</param>
/// <param name="Sound">
/// For a video done by a build with D-087, what became of its sound in the 720p version. Null for photos and for videos
/// done by earlier builds, whose 720p version may have lost working sound to D-084's fallback, so <c>process</c> checks it.
/// </param>
/// <param name="MetadataVersion">
/// Which <see cref="ProcessCommand.MetadataVersion"/> read the file's metadata (S-060, D-089). Null for rows written by
/// earlier builds, which count as version 1: what a later version reads is read again from the file's header alone.
/// </param>
public sealed record FileState(
    string Path,
    long Size,
    long ModifiedTicks,
    string? Sha256,
    FileStatus Status,
    NoVideoReason? Reason = null,
    VideoSound? Sound = null,
    int? MetadataVersion = null);

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

        // Work folders made before the left-out reason, the video's sound (D-087) or the metadata version (D-089) was
        // stored gain the column; their rows read as not recorded.
        AddColumnIfMissing("reason", "TEXT");
        AddColumnIfMissing("sound", "TEXT");
        AddColumnIfMissing("metadata_version", "INTEGER");
    }

    /// <summary>
    /// Records the work folder's mode when none is recorded yet (S-063); a recorded mode is never changed. A work folder
    /// made before S-063 gets <see cref="WorkFolderMode.KeepsOriginals"/>, which is what it always did.
    /// </summary>
    public void RecordMode(WorkFolderMode mode) => WorkFolderModes.Record(_connection, mode);

    /// <summary>The recorded mode (<see cref="WorkFolderMode.KeepsOriginals"/> when none is).</summary>
    public WorkFolderMode Mode => WorkFolderModes.Read(_connection);

    public FileState? Get(string path)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT size, modified_ticks, sha256, status, reason, sound, metadata_version FROM files WHERE path = $path";
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
            reader.IsDBNull(4) ? null : ParseReason(reader.GetString(4)),
            reader.IsDBNull(5) ? null : ParseSound(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetInt32(6));
    }

    public void Save(FileState state)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO files (path, size, modified_ticks, sha256, status, reason, sound, metadata_version)
            VALUES ($path, $size, $modified, $sha256, $status, $reason, $sound, $metadataVersion)
            ON CONFLICT (path) DO UPDATE SET
                size = excluded.size, modified_ticks = excluded.modified_ticks,
                sha256 = excluded.sha256, status = excluded.status, reason = excluded.reason, sound = excluded.sound,
                metadata_version = excluded.metadata_version
            """;
        command.Parameters.AddWithValue("$path", state.Path);
        command.Parameters.AddWithValue("$size", state.Size);
        command.Parameters.AddWithValue("$modified", state.ModifiedTicks);
        command.Parameters.AddWithValue("$sha256", (object?)state.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", state.Status.ToString());
        command.Parameters.AddWithValue("$reason", (object?)state.Reason?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$sound", (object?)state.Sound?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadataVersion", (object?)state.MetadataVersion ?? DBNull.Value);
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

    // A name from a later build reads as not recorded, so the 720p version is checked again.
    private static VideoSound? ParseSound(string value) =>
        Enum.GetNames<VideoSound>().Contains(value, StringComparer.Ordinal) ? Enum.Parse<VideoSound>(value) : null;

    private void AddColumnIfMissing(string column, string type)
    {
        using var exists = _connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM pragma_table_info('files') WHERE name = $column";
        exists.Parameters.AddWithValue("$column", column);
        if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            Execute($"ALTER TABLE files ADD COLUMN {column} {type}");
        }
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
