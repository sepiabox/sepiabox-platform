using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class FilterOptionsCacheTests
{
    private static readonly FilterOptions Loaded = new([2019], ["Lisbon"], ["HD"]);

    [Fact]
    public async Task Loads_once_and_again_only_after_five_minutes()
    {
        var clock = new MovableTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var cache = new FilterOptionsCache(clock);
        var loads = 0;
        Task<FilterOptions> Load()
        {
            loads++;
            return Task.FromResult(Loaded);
        }

        await cache.GetAsync(Load);
        clock.Now += FilterOptionsCache.Lifetime - TimeSpan.FromSeconds(1);
        await cache.GetAsync(Load);
        Assert.Equal(1, loads);

        clock.Now += TimeSpan.FromSeconds(1);
        Assert.Same(Loaded, await cache.GetAsync(Load));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Loads_again_after_a_failed_load()
    {
        var cache = new FilterOptionsCache(new MovableTime(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(() => Task.FromException<FilterOptions>(new InvalidOperationException())));

        Assert.Same(Loaded, await cache.GetAsync(() => Task.FromResult(Loaded)));
    }

    [Fact]
    public async Task Keeps_the_people_picker_s_covers_for_five_minutes_and_not_a_failed_load()
    {
        var clock = new MovableTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var cache = new FilterOptionsCache(clock);
        var loads = 0;
        Task<Dictionary<long, FaceCrop>> Load()
        {
            loads++;
            return Task.FromResult(new Dictionary<long, FaceCrop>());
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetCoversAsync(() => Task.FromException<Dictionary<long, FaceCrop>>(new InvalidOperationException())));
        await cache.GetCoversAsync(Load);
        clock.Now += FilterOptionsCache.Lifetime - TimeSpan.FromSeconds(1);
        await cache.GetCoversAsync(Load);
        Assert.Equal(1, loads);

        clock.Now += TimeSpan.FromSeconds(1);
        await cache.GetCoversAsync(Load);
        Assert.Equal(2, loads);
    }
}
