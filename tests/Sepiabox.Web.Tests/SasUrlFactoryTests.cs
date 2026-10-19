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
    private static readonly DateTimeOffset s_now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Derivative_link_is_read_only_and_expires_in_15_minutes()
    {
        using var links = new SasUrlFactory(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(s_now));

        var url = await links.ForDerivativeAsync($"{Ids.Of(1)}/thumb-256.webp");

        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal("b", query["sr"]);
        Assert.Equal(s_now.AddMinutes(15), DateTimeOffset.Parse(query["se"]!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.EndsWith($"/derivatives/{Ids.Of(1)}/thumb-256.webp", url.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Original_link_is_read_only_and_expires_in_5_minutes()
    {
        using var links = new SasUrlFactory(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(s_now));

        var url = await links.ForOriginalAsync(Ids.Of(2));

        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("r", query["sp"]);
        Assert.Equal(s_now.AddMinutes(5), DateTimeOffset.Parse(query["se"]!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.EndsWith($"/originals/{Ids.Of(2)}", url.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_account_key_links_use_one_cached_user_delegation_key()
    {
        var service = new DelegationOnlyService();
        using var links = new SasUrlFactory(service, new FixedTime(s_now));

        var first = await links.ForDerivativeAsync($"{Ids.Of(3)}/thumb-256.webp");
        var second = await links.ForOriginalAsync(Ids.Of(3));

        Assert.Equal(1, service.KeyRequests);
        foreach (var url in new[] { first, second })
        {
            var query = HttpUtility.ParseQueryString(url.Query);
            Assert.Equal("r", query["sp"]);
            Assert.Equal(DelegationOnlyService.ObjectId, query["skoid"]);
        }
    }

    [Fact]
    public void Container_names_match_what_upload_writes()
    {
        Assert.Equal(BlobLayout.OriginalsContainer, StorageContainers.Originals);
        Assert.Equal(BlobLayout.DerivativesContainer, StorageContainers.Derivatives);
    }

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
