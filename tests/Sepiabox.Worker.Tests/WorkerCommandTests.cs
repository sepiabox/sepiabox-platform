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

    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "import" },
        new[] { "import", "--watch" },
        new[] { "migrate", "--once" },
        new[] { "export" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Rejects_missing_or_unknown_arguments(string[] args)
    {
        Assert.False(WorkerCommand.TryParse(args, out _, out var error));
        Assert.NotEmpty(error);
    }
}
