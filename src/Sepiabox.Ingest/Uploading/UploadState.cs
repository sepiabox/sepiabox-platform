using Microsoft.Data.Sqlite;
using Sepiabox.Ingest.Processing;

namespace Sepiabox.Ingest.Uploading;

/// <summary>One blob that reached storage.</summary>
/// <param name="Container">Container name, such as <c>originals</c>.</param>
/// <param name="Blob">Blob name inside the container.</param>
/// <param name="Fingerprint">What was sent: the SHA-256 for an original, size and write time for a work-folder file.</param>
/// <param name="Sequence">Upload order across the whole work folder.</param>
public sealed record UploadRecord(string Container, string Blob, string Fingerprint, long Sequence);

/// <summary>
/// Remembers which blobs were already uploaded, in the work folder's <c>state.db</c> next to what
/// <c>process</c> recorded, so an interrupted upload continues where it stopped.
/// Safe to call from the parallel uploads.
/// </summary>
public sealed class UploadState : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();

    public UploadState(string databasePath)
    {
        // ReadWrite, not ReadWriteCreate: without process's state.db there is nothing to upload.
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        _connection.Open();

        using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS uploads (
                container TEXT NOT NULL,
                blob TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                PRIMARY KEY (container, blob)
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>What <c>process</c> recorded for the work folder: whether the originals are uploaded (S-063).</summary>
    public WorkFolderMode Mode
    {
        get
        {
            lock (_lock)
            {
                return WorkFolderModes.Read(_connection);
            }
        }
    }

    /// <summary>Paths where <c>process</c> found a file with this content, so the original can be read again.</summary>
    public IReadOnlyList<string> SourcePaths(string sha256)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT path FROM files WHERE sha256 = $sha256 ORDER BY path";
            command.Parameters.AddWithValue("$sha256", sha256);

            var paths = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                paths.Add(reader.GetString(0));
            }

            return paths;
        }
    }

    public bool IsUploaded(string container, string blob, string fingerprint)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM uploads WHERE container = $container AND blob = $blob AND fingerprint = $fingerprint";
            command.Parameters.AddWithValue("$container", container);
            command.Parameters.AddWithValue("$blob", blob);
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            return command.ExecuteScalar() is not null;
        }
    }

    public void MarkUploaded(string container, string blob, string fingerprint)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO uploads (container, blob, fingerprint, sequence)
                VALUES ($container, $blob, $fingerprint, (SELECT coalesce(max(sequence), 0) + 1 FROM uploads))
                ON CONFLICT (container, blob) DO UPDATE SET
                    fingerprint = excluded.fingerprint, sequence = excluded.sequence
                """;
            command.Parameters.AddWithValue("$container", container);
            command.Parameters.AddWithValue("$blob", blob);
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<UploadRecord> All()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT container, blob, fingerprint, sequence FROM uploads ORDER BY sequence";

            var records = new List<UploadRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new UploadRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
            }

            return records;
        }
    }

    public void Dispose() => _connection.Dispose();
}
