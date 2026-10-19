namespace Sepiabox.Worker;

public enum WorkerAction
{
    Migrate,
    ImportOnce,
}

/// <param name="Action">What to run.</param>
/// <param name="MigrateFirst">Apply pending migrations before importing (D-013).</param>
public sealed record WorkerCommand(WorkerAction Action, bool MigrateFirst)
{
    public const string Usage = """
        Usage: sepiabox-worker <command>

        Commands:
          migrate                    Apply the catalog's database migrations.
          import --once [--migrate]  Import manifests that are new or changed since the last import,
                                     then exit. --migrate applies migrations first.
        """;

    public static bool TryParse(string[] args, out WorkerCommand command, out string error)
    {
        command = new WorkerCommand(WorkerAction.Migrate, false);
        error = string.Empty;

        switch (args)
        {
            case ["migrate"]:
                return true;
            case ["import", .. var options] when options.All(o => o is "--once" or "--migrate"):
                if (!options.Contains("--once"))
                {
                    // Continuous, event-driven import comes with S-033.
                    error = "import needs --once.";
                    return false;
                }

                command = new WorkerCommand(WorkerAction.ImportOnce, options.Contains("--migrate"));
                return true;
            case [_, ..]:
                error = $"Unknown command or option in '{string.Join(' ', args)}'.";
                return false;
            default:
                error = "Missing command.";
                return false;
        }
    }
}
