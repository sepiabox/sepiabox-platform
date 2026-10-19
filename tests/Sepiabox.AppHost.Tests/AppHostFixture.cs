using Aspire.Hosting;

namespace Sepiabox.AppHost.Tests;

/// <summary>
/// Starts the whole AppHost (PostgreSQL, Azurite and the web) once for every test in the collection,
/// because starting containers takes far longer than the tests themselves.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(s_startupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Sepiabox_AppHost>(cts.Token);
        App = await builder.BuildAsync(cts.Token);
        await App.StartAsync(cts.Token);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("web", cts.Token);
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
