using Sepiabox.Storage;
using Sepiabox.Tests.Shared;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The lifetimes of the links the web asks the blob store to sign (D-039, D-049); how the store signs them is its own tests'.</summary>
public sealed class SasUrlFactoryTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 19, 12, 3, 20, TimeSpan.Zero);

    [Fact]
    public async Task Derivative_link_is_for_the_derivatives_container_and_expires_within_15_minutes()
    {
        var store = new RecordingStore();
        var links = new SasUrlFactory(store, new FixedTime(s_now));

        await links.ForDerivativeAsync($"{Ids.Of(1)}/thumb-256.webp");

        var signed = Assert.Single(store.Signed);
        Assert.Equal((BlobLayout.DerivativesContainer, $"{Ids.Of(1)}/thumb-256.webp"), (signed.Container, signed.Name));
        Assert.InRange(signed.ExpiresOn, s_now.AddMinutes(10), s_now.AddMinutes(15));
        Assert.Null(signed.Options);
    }

    [Fact]
    public async Task Derivative_links_signed_in_the_same_window_are_identical()
    {
        var store = new RecordingStore();
        var early = new SasUrlFactory(store, new FixedTime(new DateTimeOffset(2026, 10, 19, 12, 0, 1, TimeSpan.Zero)));
        var late = new SasUrlFactory(store, new FixedTime(new DateTimeOffset(2026, 10, 19, 12, 4, 59, TimeSpan.Zero)));

        var first = await early.ForDerivativeAsync("x/thumb-256.webp");
        var second = await late.ForDerivativeAsync("x/thumb-256.webp");

        Assert.Equal(first, second);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 12, 15, 0, TimeSpan.Zero), store.Signed[0].ExpiresOn);
    }

    [Fact]
    public async Task Video_link_lasts_four_hours_from_its_window()
    {
        var store = new RecordingStore();
        var links = new SasUrlFactory(store, new FixedTime(s_now));

        await links.ForVideoAsync($"{Ids.Of(4)}/proxy-720p.mp4");

        var signed = Assert.Single(store.Signed);
        Assert.Equal((BlobLayout.DerivativesContainer, $"{Ids.Of(4)}/proxy-720p.mp4"), (signed.Container, signed.Name));
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 16, 0, 0, TimeSpan.Zero), signed.ExpiresOn);
    }

    [Fact]
    public async Task Original_link_downloads_under_its_source_name_and_expires_in_5_minutes()
    {
        var store = new RecordingStore();
        var links = new SasUrlFactory(store, new FixedTime(s_now));

        await links.ForOriginalAsync(Ids.Of(2), new OriginalFile("Praia à tarde.jpg", "image/jpeg"));

        var signed = Assert.Single(store.Signed);
        Assert.Equal((BlobLayout.OriginalsContainer, Ids.Of(2)), (signed.Container, signed.Name));
        Assert.Equal(s_now.AddMinutes(5), signed.ExpiresOn);
        Assert.Equal(new BlobReadUrlOptions("Praia à tarde.jpg", "image/jpeg"), signed.Options);
    }

    [Fact]
    public void A_cached_redirect_expires_at_least_a_minute_before_its_link()
    {
        Assert.Equal(TimeSpan.FromSeconds(540), SasUrlFactory.RedirectCacheDuration);
        Assert.True(SasUrlFactory.RedirectCacheDuration + TimeSpan.FromMinutes(1)
            <= SasUrlFactory.DerivativeLifetime - SasUrlFactory.DerivativeWindow);
    }

    [Fact]
    public async Task Signs_on_the_real_in_memory_store_too()
    {
        var links = new SasUrlFactory(new InMemoryBlobStore(), new FixedTime(s_now));

        var url = await links.ForVideoAsync($"{Ids.Of(5)}/proxy-1080p.mp4");

        Assert.Equal("memory", url.Scheme);
        Assert.Contains($"{BlobLayout.DerivativesContainer}/{Ids.Of(5)}/proxy-1080p.mp4", url.Host + url.AbsolutePath, StringComparison.Ordinal);
    }

    /// <summary>Records what it was asked to sign.</summary>
    private sealed class RecordingStore() : DelegatingBlobStore(new InMemoryBlobStore())
    {
        public List<(string Container, string Name, DateTimeOffset ExpiresOn, BlobReadUrlOptions? Options)> Signed { get; } = [];

        public override Task<Uri> CreateReadUrlAsync(
            string container, string name, DateTimeOffset expiresOn, BlobReadUrlOptions? options = null, CancellationToken cancellationToken = default)
        {
            Signed.Add((container, name, expiresOn, options));
            return Inner.CreateReadUrlAsync(container, name, expiresOn, options, cancellationToken);
        }
    }
}
