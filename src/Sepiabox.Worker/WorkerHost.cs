using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker;

/// <summary>Runs one worker command inside a started host and always stops the host afterwards.</summary>
public static class WorkerHost
{
    public const string LoggerCategory = "Sepiabox.Worker";

    /// <summary>
    /// Once the host has started, failures are logged through the host's own logging (OpenTelemetry in
    /// Azure) before it stops. A failure to stop is logged and never replaces the command's exit code.
    /// </summary>
    public static async Task<int> RunAsync(
        IHost host, Func<IServiceProvider, ILogger, CancellationToken, Task<int>> command, CancellationTokenSource shutdown)
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        try
        {
            // Inside the try: if one hosted service fails to start, the ones already started still stop.
            // The start failure itself propagates to Program, which reports it with exit code 1.
            await host.StartAsync(shutdown.Token);

            // Ctrl+C and SIGTERM (Container Apps stopping a job) reach the command through ApplicationStopping.
            using var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping.Register(shutdown.Cancel);
            return await ExitCodes.RunAsync(() => command(host.Services, logger, shutdown.Token), logger, shutdown.Token);
        }
        finally
        {
            try
            {
                await host.StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.StopFailed(logger, ex);
            }
        }
    }
}
