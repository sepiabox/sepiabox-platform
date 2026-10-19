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
    private readonly Dictionary<string, CustomResourceSnapshot> _workers = [];

    public DistributedApplication App { get; private set; } = null!;

    /// <summary>The final state of each worker, which runs once at startup and exits.</summary>
    public IReadOnlyDictionary<string, CustomResourceSnapshot> Workers => _workers;

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(s_startupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Sepiabox_AppHost>(cts.Token);
        App = await builder.BuildAsync(cts.Token);
        await App.StartAsync(cts.Token);

        // The web waits for the migrations; if they fail it never becomes healthy, so say why up front
        // instead of timing out on the web.
        var migrate = await WaitForWorkerAsync(MigrateWorker, cts.Token);
        if (migrate.ExitCode != 0)
        {
            var exitCode = migrate.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
            throw new InvalidOperationException(
                $"{MigrateWorker} ended in state '{migrate.State?.Text}' with exit code {exitCode}; see its logs.");
        }

        await App.ResourceNotifications.WaitForResourceHealthyAsync("web", cts.Token);

        // Tests must not race the import, which also touches the database.
        await WaitForWorkerAsync(ImportWorker, cts.Token);
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }

    private async Task<CustomResourceSnapshot> WaitForWorkerAsync(string worker, CancellationToken cancellationToken)
    {
        var finished = await App.ResourceNotifications.WaitForResourceAsync(
            worker, e => KnownResourceStates.TerminalStates.Contains(e.Snapshot.State?.Text), cancellationToken);
        _workers[worker] = finished.Snapshot;
        return finished.Snapshot;
    }
}

[CollectionDefinition(Name)]
public sealed class AppHostGroup : ICollectionFixture<AppHostFixture>
{
    public const string Name = "AppHost";
}
