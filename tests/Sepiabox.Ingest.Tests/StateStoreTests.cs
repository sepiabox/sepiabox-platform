using Microsoft.Data.Sqlite;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Tests;

public class StateStoreTests
{
    [Fact]
    public void Never_changes_a_recorded_mode()
    {
        using var folder = new TempFolder();
        var database = Path.Combine(folder.Path, "state.db");

        using (var store = new StateStore(database))
        {
            Assert.Equal(WorkFolderMode.KeepsOriginals, store.Mode);
            store.RecordMode(WorkFolderMode.IndexOnly);
            store.RecordMode(WorkFolderMode.KeepsOriginals);
            Assert.Equal(WorkFolderMode.IndexOnly, store.Mode);
        }

        using var reopened = new StateStore(database);
        reopened.RecordMode(WorkFolderMode.KeepsOriginals);
        Assert.Equal(WorkFolderMode.IndexOnly, reopened.Mode);
    }

    [Fact]
    public void Returns_null_for_a_file_it_never_saw()
    {
        using var folder = new TempFolder();
        using var store = new StateStore(Path.Combine(folder.Path, "state.db"));

        Assert.Null(store.Get("/drive/unknown.jpg"));
    }

    [Fact]
    public void Remembers_files_after_being_reopened()
    {
        using var folder = new TempFolder();
        var database = Path.Combine(folder.Path, "state.db");
        var state = new FileState("/drive/a.jpg", 2048, 638_000_000_000_000_000, new string('a', 64), FileStatus.Done);

        using (var store = new StateStore(database))
        {
            store.Save(state);
        }

        using var reopened = new StateStore(database);
        Assert.Equal(state, reopened.Get("/drive/a.jpg"));
    }

    [Fact]
    public void Replaces_the_previous_state_of_a_file()
    {
        using var folder = new TempFolder();
        using var store = new StateStore(Path.Combine(folder.Path, "state.db"));
        store.Save(new FileState("/drive/a.jpg", 1, 1, null, FileStatus.Failed));

        var updated = new FileState("/drive/a.jpg", 2, 2, new string('b', 64), FileStatus.Done);
        store.Save(updated);

        Assert.Equal(updated, store.Get("/drive/a.jpg"));
    }

    [Fact]
    public void Remembers_why_a_file_was_left_out()
    {
        using var folder = new TempFolder();
        var database = Path.Combine(folder.Path, "state.db");
        var state = new FileState("/drive/song.mod", 2108, 1, new string('c', 64), FileStatus.NotVideo, NoVideoReason.TrackerModule);

        using (var store = new StateStore(database))
        {
            store.Save(state);
        }

        using var reopened = new StateStore(database);
        Assert.Equal(state, reopened.Get("/drive/song.mod"));
    }

    [Fact]
    public void Adds_the_reason_column_when_the_work_folder_was_made_by_an_earlier_build()
    {
        using var folder = new TempFolder();
        var database = Path.Combine(folder.Path, "state.db");
        using (var old = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            old.Open();
            using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE files (path TEXT PRIMARY KEY, size INTEGER NOT NULL, modified_ticks INTEGER NOT NULL, sha256 TEXT, status TEXT NOT NULL);
                INSERT INTO files VALUES ('/drive/a.jpg', 10, 20, NULL, 'Done');
                """;
            create.ExecuteNonQuery();
        }

        using (var store = new StateStore(database))
        {
            Assert.Equal(new FileState("/drive/a.jpg", 10, 20, null, FileStatus.Done), store.Get("/drive/a.jpg"));
            store.Save(new FileState("/drive/b.mod", 1, 1, null, FileStatus.NotVideo, NoVideoReason.NoStream));
        }

        // Opening again finds the column already there.
        using var reopened = new StateStore(database);
        Assert.Equal(NoVideoReason.NoStream, reopened.Get("/drive/b.mod")!.Reason);
    }

    [Fact]
    public void Remembers_what_became_of_a_video_s_sound_and_reads_none_for_rows_of_an_earlier_build()
    {
        using var folder = new TempFolder();
        var database = Path.Combine(folder.Path, "state.db");
        using (var old = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            old.Open();
            using var create = old.CreateCommand();
            // A work folder written by a build with D-084, before the sound was recorded (D-087).
            create.CommandText = """
                CREATE TABLE files (path TEXT PRIMARY KEY, size INTEGER NOT NULL, modified_ticks INTEGER NOT NULL, sha256 TEXT, status TEXT NOT NULL, reason TEXT);
                INSERT INTO files VALUES ('/drive/MOV001.MOD', 10, 20, NULL, 'Done', NULL);
                """;
            create.ExecuteNonQuery();
        }

        using (var store = new StateStore(database))
        {
            Assert.Null(store.Get("/drive/MOV001.MOD")!.Sound);
            store.Save(new FileState("/drive/MOV002.MOD", 1, 1, null, FileStatus.Done, Sound: VideoSound.Undecodable));
        }

        using var reopened = new StateStore(database);
        Assert.Equal(VideoSound.Undecodable, reopened.Get("/drive/MOV002.MOD")!.Sound);
    }

    [Fact]
    public void Remembers_the_metadata_version_and_reads_none_for_rows_of_an_earlier_build()
    {
        using var folder = new TempFolder();
        var database = Path.Combine(folder.Path, "state.db");
        using (var old = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            old.Open();
            using var create = old.CreateCommand();
            // A work folder written by a build with D-087, before the metadata version was recorded (D-089).
            create.CommandText = """
                CREATE TABLE files (path TEXT PRIMARY KEY, size INTEGER NOT NULL, modified_ticks INTEGER NOT NULL, sha256 TEXT, status TEXT NOT NULL, reason TEXT, sound TEXT);
                INSERT INTO files VALUES ('/drive/clip.mp4', 10, 20, NULL, 'Done', NULL, 'Kept');
                """;
            create.ExecuteNonQuery();
        }

        using (var store = new StateStore(database))
        {
            var earlier = store.Get("/drive/clip.mp4")!;
            Assert.Equal((VideoSound.Kept, (int?)null), (earlier.Sound, earlier.MetadataVersion));
            store.Save(earlier with { MetadataVersion = 2 });
        }

        using var reopened = new StateStore(database);
        Assert.Equal(new FileState("/drive/clip.mp4", 10, 20, null, FileStatus.Done, Sound: VideoSound.Kept, MetadataVersion: 2), reopened.Get("/drive/clip.mp4"));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("2")]
    [InlineData("ReasonFromALaterBuild")]
    public void Reads_no_reason_when_the_stored_one_is_not_a_known_name(string stored)
    {
        using var folder = new TempFolder();
        var database = Path.Combine(folder.Path, "state.db");
        using (var store = new StateStore(database))
        {
            store.Save(new FileState("/drive/b.mod", 1, 1, null, FileStatus.NotVideo, NoVideoReason.NoStream));
        }

        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE files SET reason = $reason";
            update.Parameters.AddWithValue("$reason", stored);
            update.ExecuteNonQuery();
        }

        using var reopened = new StateStore(database);
        Assert.Null(reopened.Get("/drive/b.mod")!.Reason);
    }
}
