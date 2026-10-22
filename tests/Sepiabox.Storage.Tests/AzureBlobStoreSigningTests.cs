using System.Web;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Sepiabox.Storage.Azure;

namespace Sepiabox.Storage.Tests;

/// <summary>How <see cref="AzureBlobStore"/> signs read URLs (D-039, D-049), without a network.</summary>
public sealed class AzureBlobStoreSigningTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 19, 12, 3, 20, TimeSpan.Zero);

    [Fact]
    public async Task Signs_with_the_account_key_when_the_client_has_one_read_only_for_one_blob()
    {
        using var store = new AzureBlobStore(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(s_now));

        var url = await store.CreateReadUrlAsync("derivatives", "x/thumb-256.webp", s_now.AddMinutes(12));

        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal("b", query["sr"]);
        Assert.Equal(s_now.AddMinutes(12), Expiry(url));
        Assert.EndsWith("/derivatives/x/thumb-256.webp", url.AbsolutePath, StringComparison.Ordinal);
        Assert.Null(query["rscd"]);
        Assert.Null(query["rsct"]);
    }

    [Fact]
    public async Task A_download_name_and_a_content_type_are_part_of_the_signature()
    {
        using var store = new AzureBlobStore(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(s_now));

        var url = await store.CreateReadUrlAsync("originals", "abc", s_now.AddMinutes(5), new BlobReadUrlOptions("Praia à tarde.jpg", "image/jpeg"));

        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("attachment; filename*=UTF-8''Praia%20%C3%A0%20tarde.jpg", query["rscd"]);
        Assert.Equal("image/jpeg", query["rsct"]);
    }

    [Fact]
    public async Task Refuses_a_link_that_lasts_longer_than_the_longest_it_signs()
    {
        using var store = new AzureBlobStore(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(s_now));

        await store.CreateReadUrlAsync("derivatives", "x", s_now + AzureBlobStore.MaxReadUrlLifetime);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.CreateReadUrlAsync("derivatives", "x", s_now + AzureBlobStore.MaxReadUrlLifetime + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Without_an_account_key_links_use_one_cached_user_delegation_key_over_https_only()
    {
        var service = new DelegationOnlyService();
        using var store = new AzureBlobStore(service, new FixedTime(s_now));

        var first = await store.CreateReadUrlAsync("derivatives", "x/thumb-256.webp", s_now.AddMinutes(15));
        var second = await store.CreateReadUrlAsync("originals", "abc", s_now.AddMinutes(5), new BlobReadUrlOptions("a.jpg", "image/jpeg"));

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
    public async Task Reuses_a_delegation_key_only_while_it_outlives_the_longest_link_signed_now()
    {
        var service = new DelegationOnlyService();
        var clock = new MovableTime(s_now);
        using var store = new AzureBlobStore(service, clock);

        var first = await store.CreateReadUrlAsync("derivatives", "v", s_now + AzureBlobStore.MaxReadUrlLifetime);
        clock.Now = s_now + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(54);
        var reused = await store.CreateReadUrlAsync("derivatives", "v", clock.Now + AzureBlobStore.MaxReadUrlLifetime);
        Assert.Equal(1, service.KeyRequests);

        clock.Now = s_now + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(56);
        var renewed = await store.CreateReadUrlAsync("derivatives", "v", clock.Now + AzureBlobStore.MaxReadUrlLifetime);

        Assert.Equal(2, service.KeyRequests);
        foreach (var url in new[] { first, reused, renewed })
        {
            Assert.True(KeyExpiry(url) >= Expiry(url), $"The key expires at {KeyExpiry(url):O}, before the link ({Expiry(url):O}).");
        }
    }

    private static DateTimeOffset Expiry(Uri url) => Parse(url, "se");

    private static DateTimeOffset KeyExpiry(Uri url) => Parse(url, "ske");

    private static DateTimeOffset Parse(Uri url, string parameter) =>
        DateTimeOffset.Parse(HttpUtility.ParseQueryString(url.Query)[parameter]!, System.Globalization.CultureInfo.InvariantCulture);

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

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MovableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
