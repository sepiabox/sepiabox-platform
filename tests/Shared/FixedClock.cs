namespace Sepiabox.Tests.Shared;

/// <summary>A clock that stands still in UTC, so "now" in a test never depends on when or where it runs.</summary>
internal sealed class FixedClock(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(now, DateTimeKind.Utc));

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
