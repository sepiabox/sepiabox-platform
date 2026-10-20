using Microsoft.Data.Sqlite;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

public class StateStoreTests
{
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
}
