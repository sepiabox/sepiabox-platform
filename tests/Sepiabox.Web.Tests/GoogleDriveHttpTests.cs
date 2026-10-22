using System.Net;
using System.Text;
using System.Web;
using Sepiabox.Web.Auth;
using Sepiabox.Web.Sources;

namespace Sepiabox.Web.Tests;

/// <summary>
/// <see cref="GoogleDrive"/> against a fake in place of Google (S-037, D-097): what it asks Drive for, how it counts,
/// and that the refresh token never travels in an address.
/// </summary>
public sealed class GoogleDriveHttpTests
{
    private const string RefreshToken = "1//refresh-token-never-in-an-address";

    [Fact]
    public async Task Counts_the_images_and_videos_across_three_pages_asking_for_exactly_the_media_that_is_not_in_the_trash()
    {
        var google = new FakeDriveApi();
        google.Pages["p1"] = ("p2", ["image/jpeg", "image/heic", "video/mp4"]);
        google.Pages[""] = ("p1", ["image/png", "video/quicktime"]);
        google.Pages["p2"] = ("p3", ["image/jpeg", "image/gif", "image/jpeg", "video/mp4"]);
        google.Pages["p3"] = (null, ["video/x-msvideo"]);

        var count = await Drive(google).CountMediaAsync(RefreshToken);

        Assert.Equal(new DriveMediaCount(6, 4), count);
        Assert.Equal(["", "p1", "p2", "p3"], google.FileRequests.Select(q => q["pageToken"] ?? string.Empty));
        Assert.All(google.FileRequests, q =>
        {
            Assert.Equal("(mimeType contains 'image/' or mimeType contains 'video/') and trashed = false", q["q"]);
            Assert.Equal("nextPageToken, files(mimeType)", q["fields"]);
            Assert.Equal("1000", q["pageSize"]);
        });
    }

    [Fact]
    public async Task Counts_a_file_without_a_type_as_a_photo_and_an_empty_drive_as_nothing()
    {
        var google = new FakeDriveApi();
        google.Pages[""] = (null, []);
        Assert.Equal(new DriveMediaCount(0, 0), await Drive(google).CountMediaAsync(RefreshToken));
        Assert.Single(google.FileRequests);
    }

    [Fact]
    public async Task Reads_whose_drive_it_is_and_lower_cases_the_address()
    {
        var google = new FakeDriveApi { Email = "Ana@Example.com" };

        Assert.Equal("ana@example.com", await Drive(google).GetAccountEmailAsync(RefreshToken));
        Assert.Equal("user(emailAddress)", google.AboutRequests.Single()["fields"]);
    }

    [Fact]
    public async Task A_token_google_refuses_is_an_authorization_failure()
    {
        var google = new FakeDriveApi { RefreshStatus = HttpStatusCode.BadRequest, RefreshBody = """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""" };

        await Assert.ThrowsAsync<CloudAuthorizationException>(() => Drive(google).CountMediaAsync(RefreshToken));
        await Assert.ThrowsAsync<CloudAuthorizationException>(() => Drive(google).GetAccountEmailAsync(RefreshToken));
        Assert.Empty(google.FileRequests);
    }

    [Fact]
    public async Task Revokes_with_the_token_in_the_body_of_a_form_and_none_in_the_address()
    {
        var google = new FakeDriveApi();

        await Drive(google).RevokeAsync(RefreshToken);

        var request = Assert.Single(google.Revocations);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://oauth2.googleapis.com/revoke", request.Uri.AbsoluteUri);
        Assert.Equal(string.Empty, request.Uri.Query);
        Assert.DoesNotContain("refresh-token", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Equal(RefreshToken, HttpUtility.ParseQueryString(request.Body)["token"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_revocation_throws_without_the_token_in_the_message(HttpStatusCode status)
    {
        var google = new FakeDriveApi { RevokeStatus = status };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Drive(google).RevokeAsync(RefreshToken));

        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_google_throws_without_the_token_or_the_address_in_the_message()
    {
        var google = new FakeDriveApi { RevokeFailure = new HttpRequestException($"No such host is known (oauth2.googleapis.com:443) {RefreshToken}") };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Drive(google).RevokeAsync(RefreshToken));

        Assert.DoesNotContain("refresh-token", ex.ToString(), StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }

    private static GoogleDrive Drive(FakeDriveApi google) => new(new OAuthClient("client-id.example.com", "not-a-secret"), google);

    private sealed record Captured(HttpMethod Method, Uri Uri, string? ContentType, string Body);

    /// <summary>Google's token endpoint, the Drive files and about endpoints, and the revocation, with a script for each.</summary>
    private sealed class FakeDriveApi : HttpMessageHandler
    {
        public Dictionary<string, (string? Next, string[] Types)> Pages { get; } = [];

        public string Email { get; init; } = "ana@example.com";

        public HttpStatusCode RefreshStatus { get; init; } = HttpStatusCode.OK;

        public string RefreshBody { get; init; } = """{"access_token":"fake-access-token","token_type":"Bearer","expires_in":3600}""";

        public HttpStatusCode RevokeStatus { get; init; } = HttpStatusCode.OK;

        public Exception? RevokeFailure { get; init; }

        public List<System.Collections.Specialized.NameValueCollection> FileRequests { get; } = [];

        public List<System.Collections.Specialized.NameValueCollection> AboutRequests { get; } = [];

        public List<Captured> Revocations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (uri.Host == "oauth2.googleapis.com" && uri.AbsolutePath == "/revoke")
            {
                Revocations.Add(new Captured(request.Method, uri, request.Content?.Headers.ContentType?.MediaType, body));
                return RevokeFailure is { } failure ? throw failure : Json(RevokeStatus, "{}");
            }

            if (uri.AbsolutePath.Contains("token", StringComparison.Ordinal))
            {
                return Json(RefreshStatus, RefreshBody);
            }

            var query = HttpUtility.ParseQueryString(uri.Query);
            if (uri.AbsolutePath.EndsWith("/drive/v3/about", StringComparison.Ordinal))
            {
                AboutRequests.Add(query);
                return Json(HttpStatusCode.OK, "{\"user\":{\"emailAddress\":\"" + Email + "\"}}");
            }

            if (uri.AbsolutePath.EndsWith("/drive/v3/files", StringComparison.Ordinal))
            {
                FileRequests.Add(query);
                var (next, types) = Pages[query["pageToken"] ?? string.Empty];
                var files = string.Join(',', types.Select(t => "{\"mimeType\":\"" + t + "\"}"));
                var nextPart = next is null ? string.Empty : ",\"nextPageToken\":\"" + next + "\"";
                return Json(HttpStatusCode.OK, "{\"files\":[" + files + "]" + nextPart + "}");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
