using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Clip;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The CLIP text model at start-up and in /health (S-056, D-071), with fakes.</summary>
public sealed class TextModelStartupTests
{
    [Theory]
    [InlineData(true, "Healthy")]
    [InlineData(false, "Degraded")]
    public async Task Reports_degraded_never_unhealthy_while_search_by_description_is_off(bool available, string expected)
    {
        var result = await new TextModelHealthCheck(new FakeQueryEmbedder(available)).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status.ToString());
    }

    [Theory]
    [InlineData("Production", null, true)]
    [InlineData("Development", null, false)]
    [InlineData("Production", "false", false)]
    [InlineData("Development", "true", true)]
    public void Warms_up_by_default_only_in_production(string environment, string? setting, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Search:WarmUp"] = setting })
            .Build();

        Assert.Equal(expected, SearchSetup.WarmUp(configuration, new Environment(environment)));
    }

    [Fact]
    public async Task Loads_the_model_and_embeds_one_text_before_requests()
    {
        var embedder = new FakeQueryEmbedder();
        var log = new ListLogger<TextModelWarmUp>();

        await WarmUpAsync(new TextModelWarmUp(embedder, log));

        Assert.Single(embedder.Texts);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("working set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logs_one_error_and_starts_anyway_when_the_model_is_missing()
    {
        var log = new ListLogger<TextModelWarmUp>();

        await WarmUpAsync(new TextModelWarmUp(new FakeQueryEmbedder(available: false), log));

        var error = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, error.Level);
    }

    [Theory]
    [InlineData(true, LogLevel.Error)]
    [InlineData(false, LogLevel.Warning)]
    public void Reports_an_unusable_model_as_an_error_in_production(bool failuresAreErrors, LogLevel expected)
    {
        var log = new ListLogger<ClipQueryEmbedder>();
        using var embedder = new ClipQueryEmbedder(
            "unused",
            (out string? warning, out ClipLoadFailure failure) =>
            {
                (warning, failure) = ("not the pinned SHA-256", ClipLoadFailure.Invalid);
                return null;
            },
            () => true,
            log,
            failuresAreErrors: failuresAreErrors);

        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));

        Assert.Equal(expected, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public async Task Lets_the_web_start_while_the_model_is_still_loading()
    {
        using var release = new ManualResetEventSlim();
        var embedder = new FakeQueryEmbedder(embed: _ =>
        {
            release.Wait(TimeSpan.FromSeconds(30));
            return FakeQueryEmbedder.Axis(0);
        });
        using var warmUp = new TextModelWarmUp(embedder, new ListLogger<TextModelWarmUp>());

        var start = warmUp.StartAsync(CancellationToken.None);
        var started = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(5))) == start;
        release.Set();

        Assert.True(started, "The web waited for the model to load before taking requests.");
        await warmUp.ExecuteTask!;
        Assert.Single(embedder.Texts);
    }

    [Fact]
    public async Task Logs_one_error_and_keeps_going_when_loading_throws_something_unexpected()
    {
        var log = new ListLogger<TextModelWarmUp>();
        var embedder = new FakeQueryEmbedder(embed: _ => throw new DllNotFoundException("onnxruntime"));

        await WarmUpAsync(new TextModelWarmUp(embedder, log));

        var error = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, error.Level);
    }

    [Fact]
    public async Task Keeps_the_host_running_and_reports_degraded_when_the_model_loader_throws_something_unexpected()
    {
        var log = new ListLogger<ClipQueryEmbedder>();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IQueryEmbedder>(new ClipQueryEmbedder(
                    "unused",
                    (out string? warning, out ClipLoadFailure failure) => throw new DllNotFoundException("onnxruntime"),
                    () => true,
                    log,
                    failuresAreErrors: true));
                services.AddHostedService<TextModelWarmUp>();
                services.AddHealthChecks().AddCheck<TextModelHealthCheck>(TextModelHealthCheck.Name);
            })
            .Build();

        await host.StartAsync();
        await host.Services.GetServices<IHostedService>().OfType<TextModelWarmUp>().Single().ExecuteTask!;
        var health = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        Assert.Equal(HealthStatus.Degraded, health.Status);
        var error = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Contains("DllNotFoundException", error.Message, StringComparison.Ordinal);
        await host.StopAsync();
    }

    private static async Task WarmUpAsync(TextModelWarmUp warmUp)
    {
        using (warmUp)
        {
            await warmUp.StartAsync(CancellationToken.None);
            await warmUp.ExecuteTask!;
        }
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "sepiabox-web";

        public string ContentRootPath { get; set; } = ".";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
