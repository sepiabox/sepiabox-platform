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

// No args to the host: its command-line parser would read "--once" as a setting. Connection strings
// come from the environment, which is how Aspire and Container Apps pass them.
var builder = Host.CreateApplicationBuilder();
builder.AddServiceDefaults();
builder.AddAzureBlobServiceClient("blobs");

// Read when first used, so a missing setting is reported like any other failure (exit code 1).
builder.Services.AddDbContext<CatalogDbContext>((services, options) => CatalogDbContext.Configure(
    options,
    services.GetRequiredService<IConfiguration>().GetConnectionString("catalog")
        ?? throw new InvalidOperationException("Connection string 'catalog' is not set.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ManifestValidator>();
builder.Services.AddScoped<ManifestImporter>();

using var host = builder.Build();
// Starting the host hooks Ctrl+C and SIGTERM (Container Apps stopping a job) to ApplicationStopping.
await host.StartAsync();
var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Sepiabox.Worker");

var exitCode = await ExitCodes.RunAsync(() => RunAsync(command, host.Services, logger, stopping), logger, stopping);

await host.StopAsync(CancellationToken.None);
return exitCode;

static async Task<int> RunAsync(WorkerCommand command, IServiceProvider root, ILogger logger, CancellationToken cancellationToken)
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
