using Microsoft.Extensions.Logging;

namespace Sepiabox.Worker;

/// <summary>Turns the outcome of a worker command into a process exit code that Container Apps can act on.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int Usage = 2;
    public const int Cancelled = 130;

    public static async Task<int> RunAsync(Func<Task<int>> work, ILogger logger, CancellationToken stopping)
    {
        try
        {
            return await work();
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            Log.Cancelled(logger);
            return Cancelled;
        }
        catch (Exception ex)
        {
            Log.Failed(logger, ex);
            return Failed;
        }
    }
}
