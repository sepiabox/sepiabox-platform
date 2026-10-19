using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker;
using Sepiabox.Worker.Importing;

if (args is [] or ["-h" or "--help" or "help"])
{
    Console.WriteLine(WorkerCommand.Usage);
    return 0;
}

if (!WorkerCommand.TryParse(args, out var command, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(WorkerCommand.Usage);
    return 2;
}

// No args to the host: its command-line parser would read "--once" as a setting. Connection strings
// come from the environment, which is how Aspire and Container Apps pass them.
var builder = Host.CreateApplicationBuilder();
builder.AddServiceDefaults();
builder.AddAzureBlobServiceClient("blobs");

var connectionString = builder.Configuration.GetConnectionString("catalog")
    ?? throw new InvalidOperationException("Connection string 'catalog' is not set.");
builder.Services.AddDbContext<CatalogDbContext>(options => CatalogDbContext.Configure(options, connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ManifestValidator>();
builder.Services.AddScoped<ManifestImporter>();

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

await using var scope = host.Services.CreateAsyncScope();
var services = scope.ServiceProvider;
var logger = services.GetRequiredService<ILogger<WorkerCommand>>();

if (command.Action == WorkerAction.Migrate || command.MigrateFirst)
{
    await services.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellation.Token);
    Log.MigrationsApplied(logger);
}

if (command.Action == WorkerAction.ImportOnce)
{
    var manifests = services.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(BlobLayout.ManifestsContainer);
    var result = await services.GetRequiredService<ManifestImporter>().ImportOnceAsync(manifests, cancellation.Token);
    Log.ImportFinished(logger, result.Imported, result.Unchanged, result.Problems.Count);
    return result.Problems.Count == 0 ? 0 : 1;
}

return 0;
