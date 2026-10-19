using Azure;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class MediaEndpointsTests
{
    public static TheoryData<Exception> SigningFailures =>
    [
        new RequestFailedException(403, "AuthorizationPermissionMismatch"),
        new AuthenticationFailedException("ManagedIdentityCredential authentication failed."),
        new CredentialUnavailableException("No managed identity endpoint found."),
    ];

    [Theory]
    [MemberData(nameof(SigningFailures))]
    public async Task A_failed_signing_of_a_thumbnail_is_an_uncached_503_not_a_crash(Exception failure)
    {
        var library = new FakeLibrary();
        library.Assets[Ids.Of(1)] = Details.Photo(Ids.Of(1));
        var http = new DefaultHttpContext();

        var result = await MediaEndpoints.DerivativeAsync(
            Ids.Of(1), DerivativeNames.Thumbnail, http, library, new FailingLinks(failure), NullLogger<Program>.Instance);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        Assert.Equal("60", http.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task A_failed_signing_of_the_original_explains_it_in_portuguese()
    {
        var library = new FakeLibrary();
        library.Assets[Ids.Of(2)] = Details.Photo(Ids.Of(2));

        var http = new DefaultHttpContext();

        var result = await MediaEndpoints.OriginalAsync(
            Ids.Of(2), http, library, new FailingLinks(new CredentialUnavailableException("no identity")), NullLogger<Program>.Instance);

        var text = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, text.StatusCode);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        Assert.Equal("60", http.Response.Headers.RetryAfter.ToString());
        Assert.Contains("download", text.ResponseContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preview-2048")]
    [InlineData("..")]
    [InlineData("original.jpg")]
    public async Task Only_known_derivative_names_are_served(string name)
    {
        var library = new FakeLibrary();
        library.Assets[Ids.Of(3)] = Details.Photo(Ids.Of(3));

        var result = await MediaEndpoints.DerivativeAsync(
            Ids.Of(3), name, new DefaultHttpContext(), library, new FailingLinks(), NullLogger<Program>.Instance);

        Assert.IsType<NotFound>(result);
    }
}
