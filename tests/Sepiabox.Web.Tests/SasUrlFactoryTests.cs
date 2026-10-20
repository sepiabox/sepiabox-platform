using System.Globalization;
using System.Web;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class SasUrlFactoryTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 19, 12, 3, 20, TimeSpan.Zero);

    [Fact]
    public async Task Derivative_link_is_read_only_and_expires_within_15_minutes()
    {
        using var links = new SasUrlFactory(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(s_now));

        var url = await links.ForDerivativeAsync($"{Ids.Of(1)}/thumb-256.webp");

        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal("b", query["sr"]);
        var expiry = Expiry(url);
        Assert.InRange(expiry, s_now.AddMinutes(10), s_now.AddMinutes(15));
        Assert.EndsWith($"/derivatives/{Ids.Of(1)}/thumb-256.webp", url.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Derivative_links_signed_in_the_same_window_are_identical()
    {
        var service = new BlobServiceClient("UseDevelopmentStorage=true");
        using var early = new SasUrlFactory(service, new FixedTime(new DateTimeOffset(2026, 10, 19, 12, 0, 1, TimeSpan.Zero)));
        using var late = new SasUrlFactory(service, new FixedTime(new DateTimeOffset(2026, 10, 19, 12, 4, 59, TimeSpan.Zero)));

        var first = await early.ForDerivativeAsync("x/thumb-256.webp");
        var second = await late.ForDerivativeAsync("x/thumb-256.webp");

        Assert.Equal(first, second);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 12, 15, 0, TimeSpan.Zero), Expiry(first));
    }

    [Fact]
    public async Task Video_link_is_read_only_and_lasts_four_hours_from_its_window()
    {
        using var links = new SasUrlFactory(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(s_now));

        var url = await links.ForVideoAsync($"{Ids.Of(4)}/proxy-720p.mp4");

        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal("b", query["sr"]);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 16, 0, 0, TimeSpan.Zero), Expiry(url));
        Assert.EndsWith($"/derivatives/{Ids.Of(4)}/proxy-720p.mp4", url.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reuses_a_delegation_key_only_while_it_outlives_a_video_link_signed_now()
    {
        var service = new DelegationOnlyService();
        var clock = new MovableTime(s_now);
        using var links = new SasUrlFactory(service, clock);

        var first = await links.ForVideoAsync($"{Ids.Of(5)}/proxy-720p.mp4");
        clock.Now = s_now + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(54);
        var reused = await links.ForVideoAsync($"{Ids.Of(5)}/proxy-720p.mp4");
        Assert.Equal(1, service.KeyRequests);

        clock.Now = s_now + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(56);
        var renewed = await links.ForVideoAsync($"{Ids.Of(5)}/proxy-720p.mp4");

        Assert.Equal(2, service.KeyRequests);
        foreach (var url in new[] { first, reused, renewed })
        {
            Assert.True(KeyExpiry(url) >= Expiry(url), $"The key expires at {KeyExpiry(url):O}, before the link ({Expiry(url):O}).");
        }
    }

    [Fact]
    public async Task Delegation_signed_links_are_https_only_when_storage_is_https()
    {
        using var links = new SasUrlFactory(new DelegationOnlyService(), new FixedTime(s_now));

        var thumbnail = await links.ForDerivativeAsync($"{Ids.Of(6)}/thumb-256.webp");
        var video = await links.ForVideoAsync($"{Ids.Of(6)}/proxy-720p.mp4");

        Assert.Equal("https", HttpUtility.ParseQueryString(thumbnail.Query)["spr"]);
        Assert.Equal("https", HttpUtility.ParseQueryString(video.Query)["spr"]);
    }

    [Fact]
    public async Task Original_link_downloads_under_its_source_name_and_expires_in_5_minutes()
    {
        using var links = new SasUrlFactory(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(s_now));

        var url = await links.ForOriginalAsync(Ids.Of(2), new OriginalFile("Praia à tarde.jpg", "image/jpeg"));

        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal(s_now.AddMinutes(5), Expiry(url));
        Assert.Equal("attachment; filename*=UTF-8''Praia%20%C3%A0%20tarde.jpg", query["rscd"]);
        Assert.Equal("image/jpeg", query["rsct"]);
        Assert.EndsWith($"/originals/{Ids.Of(2)}", url.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_account_key_links_use_one_cached_user_delegation_key_over_https_only()
    {
        var service = new DelegationOnlyService();
        using var links = new SasUrlFactory(service, new FixedTime(s_now));

        var first = await links.ForDerivativeAsync($"{Ids.Of(3)}/thumb-256.webp");
        var second = await links.ForOriginalAsync(Ids.Of(3), new OriginalFile("a.jpg", "image/jpeg"));

        Assert.Equal(1, service.KeyRequests);
        foreach (var url in new[] { first, second })
        {
            var query = HttpUtility.ParseQueryString(url.Query);
            Assert.Equal("r", query["sp"]);
            Assert.Equal("https", query["spr"]);
            Assert.Equal(DelegationOnlyService.ObjectId, query["skoid"]);
        }
    }

    [Fact]
    public void A_cached_redirect_expires_at_least_a_minute_before_its_link()
    {
        Assert.Equal(TimeSpan.FromSeconds(540), SasUrlFactory.RedirectCacheDuration);
        Assert.True(SasUrlFactory.RedirectCacheDuration + TimeSpan.FromMinutes(1)
            <= SasUrlFactory.DerivativeLifetime - SasUrlFactory.DerivativeWindow);
    }

    [Fact]
    public void Container_names_match_what_upload_writes()
    {
        Assert.Equal(BlobLayout.OriginalsContainer, StorageContainers.Originals);
        Assert.Equal(BlobLayout.DerivativesContainer, StorageContainers.Derivatives);
    }

    private static DateTimeOffset Expiry(Uri url) =>
        DateTimeOffset.Parse(HttpUtility.ParseQueryString(url.Query)["se"]!, CultureInfo.InvariantCulture);

    private static DateTimeOffset KeyExpiry(Uri url) =>
        DateTimeOffset.Parse(HttpUtility.ParseQueryString(url.Query)["ske"]!, CultureInfo.InvariantCulture);

    /// <summary>Signs in with a token (like a managed identity), so only user delegation SAS is possible.</summary>
    private sealed class DelegationOnlyService()
        : BlobServiceClient(new Uri("https://sepiaboxtest.blob.core.windows.net"), new NoToken())
    {
        public const string ObjectId = "00000000-0000-0000-0000-000000000001";

        public int KeyRequests { get; private set; }

        public override Task<Response<UserDelegationKey>> GetUserDelegationKeyAsync(
            DateTimeOffset? startsOn, DateTimeOffset expiresOn, CancellationToken cancellationToken = default)
        {
            KeyRequests++;
            var key = BlobsModelFactory.UserDelegationKey(
                ObjectId, "00000000-0000-0000-0000-000000000002", startsOn ?? s_now, expiresOn,
                "b", "2025-07-05", Convert.ToBase64String(new byte[32]));
            return Task.FromResult(Response.FromValue(key, null!));
        }
    }

    private sealed class NoToken : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No network in unit tests.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No network in unit tests.");
    }
}
