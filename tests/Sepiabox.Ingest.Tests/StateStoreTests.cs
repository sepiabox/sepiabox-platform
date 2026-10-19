using Sepiabox.Ingest.Processing;

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
}
