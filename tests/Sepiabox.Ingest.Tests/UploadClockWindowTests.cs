using Sepiabox.Ingest.Uploading;
using Sepiabox.Storage;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// The window <c>upload</c> allows between this computer's clock and the store's (S-022, D-081): the server's time is in
/// whole seconds rounded down, so it may be a second before the request started and still be on time.
/// </summary>
public sealed class UploadClockWindowTests
{
    private static readonly DateTime s_now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-301)]
    [InlineData(-300)]
    [InlineData(0)]
    [InlineData(300)]
    public async Task Accepts_a_server_time_up_to_the_edge_of_the_window(int secondsFromNow)
    {
        await UploadCommand.CheckClockAsync(StoreAt(secondsFromNow), "manifests", new FixedClock(s_now), CancellationToken.None);
    }

    [Theory]
    [InlineData(-302, true)]
    [InlineData(-3_600, true)]
    [InlineData(301, false)]
    [InlineData(3_600, false)]
    public async Task Refuses_a_server_time_past_the_edge_and_says_which_way_the_clock_is_off(int secondsFromNow, bool computerAhead)
    {
        var failure = await Assert.ThrowsAsync<ClockSkewException>(
            () => UploadCommand.CheckClockAsync(StoreAt(secondsFromNow), "manifests", new FixedClock(s_now), CancellationToken.None));

        Assert.Equal(computerAhead, failure.Skew > TimeSpan.Zero);
        Assert.Equal(-secondsFromNow, failure.Skew.TotalSeconds);
    }

    [Fact]
    public async Task Skips_the_check_when_the_store_gives_no_time()
    {
        await UploadCommand.CheckClockAsync(StoreAt(null), "manifests", new FixedClock(s_now), CancellationToken.None);
    }

    private static SetTimeStore StoreAt(int? secondsFromNow) =>
        new(secondsFromNow is { } seconds ? new DateTimeOffset(s_now, TimeSpan.Zero).AddSeconds(seconds) : null);

    private sealed class SetTimeStore(DateTimeOffset? time) : DelegatingBlobStore(new InMemoryBlobStore())
    {
        public override Task<DateTimeOffset?> GetServerTimeAsync(string container, CancellationToken cancellationToken = default) => Task.FromResult(time);
    }
}
