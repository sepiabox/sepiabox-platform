using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker;
using Sepiabox.Worker.Azure;
using Sepiabox.Worker.Events;
using Sepiabox.Worker.Importing;
using Sepiabox.Worker.Places;

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
    if (command.Action is WorkerAction.ImportOnce or WorkerAction.ImportFromQueue)
    {
        builder.AddAzureBlobServiceClient("blobs");
        builder.Services.AddSingleton<ManifestValidator>();
        // D-016's same-person similarity unless configured (S-019).
        builder.Services.AddSingleton(FaceAssignerOptions.For(
            builder.Configuration.GetValue("People:SameFaceThreshold", FaceAssignerOptions.DefaultThreshold)));
        builder.Services.AddScoped<ManifestImporter>();
    }

    if (command.Action is WorkerAction.ImportOnce or WorkerAction.ImportFromQueue or WorkerAction.PlacesRefresh or WorkerAction.GeoNamesUpdate)
    {
        // The PostGIS implementation (D-051), on the same context as the importer.
        builder.Services.AddScoped<IPlaceResolver, PostgisPlaceResolver>();
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
        var db = services.GetRequiredService<CatalogDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        Log.MigrationsApplied(logger);

        // After the migrations, so the role is granted every table they created.
        if (AppRoleSettings.FromConfiguration(services.GetRequiredService<IConfiguration>()) is { } appRole)
        {
            await CatalogAppRole.EnsureAsync(
                (NpgsqlConnection)db.Database.GetDbConnection(), appRole.Name, appRole.Password, cancellationToken);
            Log.AppRoleEnsured(logger, appRole.Name);
        }
    }

    if (command is { Action: WorkerAction.GeoNamesLoad, GeoNames: { } source })
    {
        // Read the small lookups first, so a wrong path fails before the table is touched.
        var admin1 = GeoNamesFiles.ReadAdmin1(source.Admin1);
        var countries = GeoNamesFiles.ReadCountries(source.Countries);
        var loaded = await new GeoNamesLoader(services.GetRequiredService<CatalogDbContext>())
            .LoadAsync(GeoNamesFiles.ReadCities(source.Cities), admin1, countries, cancellationToken);
        Log.GeoNamesLoaded(logger, loaded);
        return ExitCodes.Success;
    }

    if (command is { Action: WorkerAction.GeoNamesDownload, Folder: { } folder })
    {
        using var http = GeoNamesDownloader.CreateClient();
        await new GeoNamesDownloader(http, logger).DownloadAsync(folder, cancellationToken);
        return ExitCodes.Success;
    }

    if (command.Action == WorkerAction.GeoNamesUpdate)
    {
        using var http = GeoNamesDownloader.CreateClient();
        await new GeoNamesUpdate(
                services.GetRequiredService<CatalogDbContext>(),
                new GeoNamesDownloader(http, logger),
                services.GetRequiredService<IPlaceResolver>(),
                GeoNamesLoadGuard.Default,
                logger)
            .RunAsync(cancellationToken);
        return ExitCodes.Success;
    }

    if (command.Action == WorkerAction.PlacesRefresh)
    {
        var named = await new PlacesRefresh(
                services.GetRequiredService<CatalogDbContext>(), services.GetRequiredService<IPlaceResolver>(), command.AllPlaces)
            .RunAsync(cancellationToken);
        Log.PlacesRefreshed(logger, named);
        return ExitCodes.Success;
    }

    if (command.Action == WorkerAction.ImportFromQueue)
    {
        // The Azure implementation of the queue (D-051): Event Grid's events, delivered to Service Bus.
        await using var queue = CreateServiceBusQueue(services.GetRequiredService<IConfiguration>());
        var manifestsContainer = services.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(BlobLayout.ManifestsContainer);
        var importer = services.GetRequiredService<ManifestImporter>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var options = QueueImportOptions.Default with
        {
            MaxDeliveryCount = configuration.GetValue("ServiceBus:MaxDeliveryCount", QueueImportOptions.Default.MaxDeliveryCount),
        };
        var fromQueue = await new QueueImport(
                queue,
                (blobName, token) => importer.ImportOneAsync(manifestsContainer, blobName, token),
                options,
                services.GetRequiredService<ILogger<QueueImport>>())
            .RunAsync(cancellationToken);
        Log.ImportFinished(logger, fromQueue.Imported, fromQueue.Unchanged, fromQueue.Problems.Count, fromQueue.Warnings.Count);
        return fromQueue.Problems.Count == 0 ? ExitCodes.Success : ExitCodes.Failed;
    }

    if (command.Action != WorkerAction.ImportOnce)
    {
        return ExitCodes.Success;
    }

    var manifests = services.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(BlobLayout.ManifestsContainer);
    var result = await services.GetRequiredService<ManifestImporter>().ImportOnceAsync(manifests, cancellationToken);
    Log.ImportFinished(logger, result.Imported, result.Unchanged, result.Problems.Count, result.Warnings.Count);
    return result.Problems.Count == 0 ? ExitCodes.Success : ExitCodes.Failed;
}

static ServiceBusBlobEventQueue CreateServiceBusQueue(IConfiguration configuration)
{
    var ns = configuration["ServiceBus:Namespace"] ?? throw new InvalidOperationException("ServiceBus:Namespace is not set.");
    var queue = configuration["ServiceBus:Queue"] ?? throw new InvalidOperationException("ServiceBus:Queue is not set.");
    // The job's managed identity, named by AZURE_CLIENT_ID, which the AppHost sets. The client is owned by the
    // receiver's queue only for this command, so it is disposed with it.
    var client = new ServiceBusClient(ns, new DefaultAzureCredential());
    var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions
    {
        ReceiveMode = ServiceBusReceiveMode.PeekLock,
        PrefetchCount = 0,
    });
    return new ServiceBusBlobEventQueue(client, receiver);
}
