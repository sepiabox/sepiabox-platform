using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sepiabox.AppHost.Tests;

/// <summary>
/// Starts the whole AppHost (PostgreSQL, Azurite, the workers and the web) once for every test in the
/// collection, because starting containers takes far longer than the tests themselves.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    public const string MigrateWorker = "worker-migrate";
    public const string ImportWorker = "worker-import";

    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;

    /// <summary>The final state of each worker, which runs once at startup and exits.</summary>
    public Dictionary<string, CustomResourceSnapshot> Workers { get; } = [];

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(s_startupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Sepiabox_AppHost>(cts.Token);
        App = await builder.BuildAsync(cts.Token);
        await App.StartAsync(cts.Token);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("web", cts.Token);

        // Tests must not race the workers: both touch the database (migrations, CREATE EXTENSION).
        foreach (var worker in new[] { MigrateWorker, ImportWorker })
        {
            var finished = await App.ResourceNotifications.WaitForResourceAsync(
                worker, e => KnownResourceStates.TerminalStates.Contains(e.Snapshot.State?.Text), cts.Token);
            Workers[worker] = finished.Snapshot;
        }
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AppHostGroup : ICollectionFixture<AppHostFixture>
{
    public const string Name = "AppHost";
}
