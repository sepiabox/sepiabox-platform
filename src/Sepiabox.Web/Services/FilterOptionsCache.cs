namespace Sepiabox.Web.Services;

/// <summary>
/// The filter bar's years, cities and sources, kept for <see cref="Lifetime"/> so a timeline render does not scan
/// the catalog three times (S-013): an import shows up in the bar within minutes, which is soon enough.
/// </summary>
public sealed class FilterOptionsCache(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private (DateTimeOffset Expires, Task<FilterOptions> Options)? _entry;

    /// <summary>The cached options, or <paramref name="load"/>'s while they are fresh; a failed load is not kept.</summary>
    public Task<FilterOptions> GetAsync(Func<Task<FilterOptions>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            if (_entry is { } entry && entry.Expires > now && !entry.Options.IsFaulted && !entry.Options.IsCanceled)
            {
                return entry.Options;
            }

            var options = load();
            _entry = (now + Lifetime, options);
            return options;
        }
    }
}
