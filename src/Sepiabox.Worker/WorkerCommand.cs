using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Worker;

public enum WorkerAction
{
    Migrate,
    ImportOnce,
    ImportFromQueue,
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
          import --from-queue        Import the manifests named by the storage events waiting in the
                                     queue (ServiceBus:Namespace, ServiceBus:Queue), then exit when
                                     it is empty. Never migrates (D-063).
        """;

    public static bool TryParse(string[] args, [NotNullWhen(true)] out WorkerCommand? command, out string error)
    {
        command = null;
        error = string.Empty;

        switch (args)
        {
            case ["migrate"]:
                command = new WorkerCommand(WorkerAction.Migrate, false);
                return true;
            case ["import", "--from-queue"]:
                command = new WorkerCommand(WorkerAction.ImportFromQueue, false);
                return true;
            case ["import", .. var options] when options.All(o => o is "--once" or "--migrate"):
                if (!options.Contains("--once"))
                {
                    error = "import needs --once or --from-queue.";
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
