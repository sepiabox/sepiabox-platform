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
        // What Azure.Core's retry policy throws when storage stays unreachable.
        new AggregateException("Retry failed after 4 tries.", new RequestFailedException(503, "ServerBusy"), new RequestFailedException(503, "ServerBusy")),
        new AggregateException("Retry failed after 2 tries.", new TaskCanceledException("timeout"), new RequestFailedException(500, "InternalError")),
        // A network timeout, while the browser is still waiting.
        new TaskCanceledException("The operation was canceled."),
    ];

    [Fact]
    public async Task A_programming_error_inside_a_retry_failure_is_not_hidden_as_a_503()
    {
        var library = new FakeLibrary();
        library.Assets[Ids.Of(1)] = Details.Photo(Ids.Of(1));
        var failure = new AggregateException(new RequestFailedException(503, "ServerBusy"), new InvalidOperationException("bug"));

        await Assert.ThrowsAsync<AggregateException>(() => MediaEndpoints.DerivativeAsync(
            Ids.Of(1), DerivativeNames.Thumbnail, new DefaultHttpContext(), library, new FailingLinks(failure), NullLogger<Program>.Instance));
    }

    [Fact]
    public async Task A_browser_that_went_away_is_not_answered_with_a_503()
    {
        var library = new FakeLibrary();
        library.Assets[Ids.Of(1)] = Details.Photo(Ids.Of(1));
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var http = new DefaultHttpContext { RequestAborted = aborted.Token };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaEndpoints.DerivativeAsync(
            Ids.Of(1), DerivativeNames.Thumbnail, http, library,
            new FailingLinks(new TaskCanceledException("client gone", null, aborted.Token)), NullLogger<Program>.Instance));
    }

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

    [Theory]
    [InlineData(Cultures.English, "Couldn't prepare the download. Please try again shortly.")]
    [InlineData(Cultures.BrazilianPortuguese, "Não foi possível preparar o download. Tente novamente daqui a pouco.")]
    public async Task A_failed_signing_of_the_original_explains_it_in_the_culture(string culture, string expected)
    {
        using var scope = Cultures.Use(culture);
        var library = new FakeLibrary();
        library.Assets[Ids.Of(2)] = Details.Photo(Ids.Of(2));

        var http = new DefaultHttpContext();

        var result = await MediaEndpoints.OriginalAsync(
            Ids.Of(2), http, library, new FailingLinks(new CredentialUnavailableException("no identity")), Cultures.Text(),
            NullLogger<Program>.Instance);

        var text = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, text.StatusCode);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        Assert.Equal("60", http.Response.Headers.RetryAfter.ToString());
        Assert.Equal(expected, text.ResponseContent);
    }

    [Theory]
    [InlineData("preview-4096")]
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
