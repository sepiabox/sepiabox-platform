namespace Sepiabox.Worker.Tests;

public sealed class WorkerCommandTests
{
    [Fact]
    public void Parses_migrate()
    {
        Assert.True(WorkerCommand.TryParse(["migrate"], out var command, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.Migrate, false), command);
    }

    [Theory]
    [InlineData(false, "import", "--once")]
    [InlineData(true, "import", "--once", "--migrate")]
    [InlineData(true, "import", "--migrate", "--once")]
    public void Parses_import_once_with_optional_migrate(bool migrateFirst, params string[] args)
    {
        Assert.True(WorkerCommand.TryParse(args, out var command, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.ImportOnce, migrateFirst), command);
    }

    [Fact]
    public void Parses_import_from_queue_which_never_migrates()
    {
        Assert.True(WorkerCommand.TryParse(["import", "--from-queue"], out var command, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.ImportFromQueue, false), command);
    }

    [Theory]
    [InlineData("geonames", "load", "cities500.zip", "--admin1", "admin1CodesASCII.txt", "--countries", "countryInfo.txt")]
    [InlineData("geonames", "load", "cities500.zip", "--countries", "countryInfo.txt", "--admin1", "admin1CodesASCII.txt")]
    public void Parses_geonames_load_with_its_three_files(params string[] args)
    {
        Assert.True(WorkerCommand.TryParse(args, out var command, out _));
        Assert.Equal(
            new WorkerCommand(
                WorkerAction.GeoNamesLoad, false, new GeoNamesSource("cities500.zip", "admin1CodesASCII.txt", "countryInfo.txt")),
            command);
    }

    [Fact]
    public void Parses_geonames_download_and_update()
    {
        Assert.True(WorkerCommand.TryParse(["geonames", "download", "dump"], out var download, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.GeoNamesDownload, false, Folder: "dump"), download);
        Assert.True(WorkerCommand.TryParse(["geonames", "update"], out var update, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.GeoNamesUpdate, false), update);
    }

    [Fact]
    public void Parses_events_rebuild_and_nothing_after_it()
    {
        Assert.True(WorkerCommand.TryParse(["events", "rebuild"], out var command, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.EventsRebuild, false), command);
        Assert.False(WorkerCommand.TryParse(["events", "rebuild", "--all"], out _, out _));
        Assert.False(WorkerCommand.TryParse(["events"], out _, out _));
    }

    [Fact]
    public void Parses_events_classify_and_nothing_after_it()
    {
        Assert.True(WorkerCommand.TryParse(["events", "classify"], out var command, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.EventsClassify, false), command);
        Assert.False(WorkerCommand.TryParse(["events", "classify", "--all"], out _, out _));
    }

    [Fact]
    public void Parses_assets_delete_as_a_dry_run_or_with_yes_and_the_count_expected()
    {
        Assert.True(WorkerCommand.TryParse(["assets", "delete", "--source", "wrong-drive"], out var dry, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.AssetsDelete, false, Source: "wrong-drive"), dry);
        Assert.True(WorkerCommand.TryParse(["assets", "delete", "--source", "HD da Ana", "--yes", "--expect", "3"], out var real, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.AssetsDelete, false, Source: "HD da Ana", Yes: true, Expect: 3), real);
        Assert.True(WorkerCommand.TryParse(["assets", "delete", "--expect", "0", "--source", "x", "--yes"], out var zero, out _));
        Assert.Equal(0, zero.Expect);
    }

    [Theory]
    [InlineData("assets", "delete")]
    [InlineData("assets", "delete", "--source")]
    [InlineData("assets", "delete", "--source", "--yes")]
    [InlineData("assets", "delete", "--source", " ")]
    [InlineData("assets", "delete", "--source", "x", "--yes")]
    [InlineData("assets", "delete", "--source", "x", "--expect", "2")]
    [InlineData("assets", "delete", "--source", "x", "--yes", "--expect")]
    [InlineData("assets", "delete", "--source", "x", "--yes", "--expect", "-1")]
    [InlineData("assets", "delete", "--source", "x", "--yes", "--expect", "two")]
    [InlineData("assets", "delete", "--source", "x", "--source", "y")]
    [InlineData("assets", "delete", "--source", "x", "--all")]
    [InlineData("assets")]
    [InlineData("assets", "list")]
    public void Refuses_assets_delete_without_a_source_or_with_only_one_of_yes_and_expect(params string[] args)
    {
        Assert.False(WorkerCommand.TryParse(args, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parses_places_refresh()
    {
        Assert.True(WorkerCommand.TryParse(["places", "refresh"], out var command, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.PlacesRefresh, false), command);
        Assert.True(WorkerCommand.TryParse(["places", "refresh", "--all"], out var all, out _));
        Assert.Equal(new WorkerCommand(WorkerAction.PlacesRefresh, false, AllPlaces: true), all);
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "import" },
        new[] { "import", "--watch" },
        new[] { "import", "--from-queue", "--migrate" },
        new[] { "import", "--from-queue", "--once" },
        new[] { "import", "--once", "--from-queue" },
        new[] { "migrate", "--once" },
        new[] { "export" },
        new[] { "geonames" },
        new[] { "geonames", "load" },
        new[] { "geonames", "load", "cities500.zip" },
        new[] { "geonames", "load", "cities500.zip", "--admin1", "a.txt" },
        new[] { "geonames", "load", "cities500.zip", "--admin1", "a.txt", "--countries" },
        new[] { "geonames", "load", "cities500.zip", "--admin1", "--countries", "c.txt" },
        new[] { "geonames", "load", "cities500.zip", "--admin1", "a.txt", "--admin1", "b.txt", "--countries", "c.txt" },
        new[] { "geonames", "load", "cities500.zip", "--admin1", "a.txt", "--countries", "c.txt", "--migrate", "x" },
        new[] { "geonames", "load", "--admin1", "a.txt", "--countries", "c.txt" },
        new[] { "geonames", "download" },
        new[] { "geonames", "download", "--to", "dump" },
        new[] { "geonames", "download", "a", "b" },
        new[] { "geonames", "update", "--migrate" },
        new[] { "places" },
        new[] { "places", "refresh", "--force" },
        new[] { "places", "refresh", "--all", "--all" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Rejects_missing_or_unknown_arguments(string[] args)
    {
        Assert.False(WorkerCommand.TryParse(args, out var command, out var error));
        Assert.Null(command);
        Assert.NotEmpty(error);
    }
}
