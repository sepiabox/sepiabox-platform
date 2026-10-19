using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker;
using Sepiabox.Worker.Importing;

if (args is ["-h" or "--help" or "help"])
{
    Console.WriteLine(WorkerCommand.Usage);
    return ExitCodes.Success;
}

if (!WorkerCommand.TryParse(args, out var command, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(WorkerCommand.Usage);
    return ExitCodes.Usage;
}

// Only for failures before the host, and its configured logging, exist (building or starting it);
// once started, WorkerHost logs through the host.
using var startupLoggers = LoggerFactory.Create(logging => logging.AddSimpleConsole());
using var shutdown = new CancellationTokenSource();

return await ExitCodes.RunAsync(
    () => RunHostAsync(command, shutdown),
    startupLoggers.CreateLogger(WorkerHost.LoggerCategory),
    shutdown.Token);

static async Task<int> RunHostAsync(WorkerCommand command, CancellationTokenSource shutdown)
{
    // No args to the host: its command-line parser would read "--once" as a setting. Connection strings
    // come from the environment, which is how Aspire and Container Apps pass them.
    var builder = Host.CreateApplicationBuilder();
    builder.AddServiceDefaults();
    if (command.Action == WorkerAction.ImportOnce)
    {
        builder.AddAzureBlobServiceClient("blobs");
        builder.Services.AddSingleton<ManifestValidator>();
        builder.Services.AddScoped<ManifestImporter>();
    }

    // Read when first used, so a missing setting is reported like any other failure (exit code 1).
    builder.Services.AddDbContext<CatalogDbContext>((services, options) => CatalogDbContext.Configure(
        options,
        services.GetRequiredService<IConfiguration>().GetConnectionString("catalog")
            ?? throw new InvalidOperationException("Connection string 'catalog' is not set.")));
    builder.Services.AddSingleton(TimeProvider.System);

    using var host = builder.Build();
    return await WorkerHost.RunAsync(
        host, (services, logger, cancellationToken) => RunCommandAsync(command, services, logger, cancellationToken), shutdown);
}

static async Task<int> RunCommandAsync(WorkerCommand command, IServiceProvider root, ILogger logger, CancellationToken cancellationToken)
{
    await using var scope = root.CreateAsyncScope();
    var services = scope.ServiceProvider;

    if (command.Action == WorkerAction.Migrate || command.MigrateFirst)
    {
        await services.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
        Log.MigrationsApplied(logger);
    }

    if (command.Action != WorkerAction.ImportOnce)
    {
        return ExitCodes.Success;
    }

    var manifests = services.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(BlobLayout.ManifestsContainer);
    var result = await services.GetRequiredService<ManifestImporter>().ImportOnceAsync(manifests, cancellationToken);
    Log.ImportFinished(logger, result.Imported, result.Unchanged, result.Problems.Count);
    return result.Problems.Count == 0 ? ExitCodes.Success : ExitCodes.Failed;
}
