using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker.Tests;

public sealed class WorkerHostTests : IDisposable
{
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose() => _logs.Dispose();

    [Fact]
    public async Task A_failure_after_start_is_logged_through_the_host_logging()
    {
        using var host = BuildHost();
        using var shutdown = new CancellationTokenSource();

        var code = await WorkerHost.RunAsync(host, (_, _, _) => throw new InvalidOperationException("database is down"), shutdown);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains(_logs.Entries, e => e.Category == "Sepiabox.Worker" && e.Level == LogLevel.Error && e.Exception?.Message == "database is down");
    }

    [Fact]
    public async Task A_failing_stop_is_logged_and_does_not_change_the_exit_code()
    {
        using var host = BuildHost(services => services.AddHostedService<FailsOnStop>());
        using var shutdown = new CancellationTokenSource();

        var code = await WorkerHost.RunAsync(host, (_, _, _) => Task.FromResult(ExitCodes.Success), shutdown);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains(_logs.Entries, e => e.Category == "Sepiabox.Worker" && e.Level == LogLevel.Warning && e.Exception is not null);
    }

    [Fact]
    public async Task Stopping_the_host_cancels_the_command()
    {
        using var host = BuildHost();
        using var shutdown = new CancellationTokenSource();

        var code = await WorkerHost.RunAsync(
            host,
            async (services, _, cancellationToken) =>
            {
                // What Ctrl+C or SIGTERM do through the host's console lifetime.
                services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return ExitCodes.Success;
            },
            shutdown);

        Assert.Equal(ExitCodes.Cancelled, code);
    }

    private IHost BuildHost(Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);
        configure?.Invoke(builder.Services);
        return builder.Build();
    }

    private sealed class FailsOnStop : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("stop failed");
    }

    private sealed record LogEntry(string Category, LogLevel Level, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, exception));
        }
    }
}
