using Microsoft.Extensions.Logging.Abstractions;

namespace Sepiabox.Worker.Tests;

public sealed class ExitCodesTests
{
    [Fact]
    public async Task Returns_the_code_of_the_work()
    {
        Assert.Equal(1, await ExitCodes.RunAsync(() => Task.FromResult(1), NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_130_when_the_host_is_stopping()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var code = await ExitCodes.RunAsync(
            () => Task.FromCanceled<int>(stopping.Token), NullLogger.Instance, stopping.Token);

        Assert.Equal(ExitCodes.Cancelled, code);
    }

    [Fact]
    public async Task Returns_1_for_an_unhandled_failure()
    {
        var code = await ExitCodes.RunAsync(
            () => throw new InvalidOperationException("database is down"), NullLogger.Instance, CancellationToken.None);

        Assert.Equal(ExitCodes.Failed, code);
    }

    [Fact]
    public async Task A_cancellation_the_host_did_not_ask_for_is_a_failure()
    {
        // A timeout inside a library also throws OperationCanceledException; that is not the user stopping.
        var code = await ExitCodes.RunAsync(
            () => throw new OperationCanceledException(), NullLogger.Instance, CancellationToken.None);

        Assert.Equal(ExitCodes.Failed, code);
    }
}
