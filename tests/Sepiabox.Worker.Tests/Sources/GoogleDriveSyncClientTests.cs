using System.Net;
using System.Text;
using System.Web;
using Sepiabox.Catalog;
using Sepiabox.Worker.Sources;

namespace Sepiabox.Worker.Tests.Sources;

/// <summary><see cref="GoogleDriveSyncClient"/> against a fake in place of Google (S-038): what it asks Drive for and how it reads the answer.</summary>
public sealed class GoogleDriveSyncClientTests
{
    private const string RefreshToken = "1//refresh-token-never-in-a-message";

    [Fact]
    public async Task Asks_for_the_start_token_and_for_the_media_that_is_not_in_the_trash_a_page_at_a_time()
    {
        var google = new FakeGoogle();
        google.Json["/drive/v3/changes/startPageToken"] = """{"startPageToken":"42"}""";
        google.Json["/drive/v3/files"] = """
            {"nextPageToken":"next","files":[
              {"id":"a","name":"Praia.jpg","mimeType":"image/jpeg","size":"1234","modifiedTime":"2020-05-01T10:00:00.000Z","md5Checksum":"abc","parents":["f1"],"webViewLink":"https://drive.google.com/file/d/a/view"},
              {"id":"b","name":"clip","mimeType":"video/mp4"}]}
            """;
        var client = Client(google);

        var token = await client.GetStartPageTokenAsync(RefreshToken);
        var page = await client.ListMediaAsync(RefreshToken, "from-here");

        Assert.Equal("42", token);
        Assert.Equal("next", page.NextPageToken);
        var first = page.Files[0];
        Assert.Equal(("a", "Praia.jpg", "image/jpeg", 1234L, "abc", "https://drive.google.com/file/d/a/view"), (first.Id, first.Name, first.MimeType, first.Size, first.Md5, first.WebViewLink));
        Assert.Equal(new DateTimeOffset(2020, 5, 1, 10, 0, 0, TimeSpan.Zero), first.ModifiedTime);
        Assert.Equal(["f1"], first.Parents);
        Assert.Equal(("b", "video/mp4", 0), (page.Files[1].Id, page.Files[1].MimeType, page.Files[1].Parents.Count));
        var query = google.Requests.Single(r => r.Path == "/drive/v3/files").Query;
        Assert.Equal("(mimeType contains 'image/' or mimeType contains 'video/') and trashed = false and 'me' in owners", query["q"]);
        Assert.Equal("from-here", query["pageToken"]);
        Assert.Contains("md5Checksum", query["fields"], StringComparison.Ordinal);
        Assert.Contains("ownedByMe", query["fields"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_out_a_change_to_a_file_somebody_else_owns()
    {
        var google = new FakeGoogle();
        google.Json["/drive/v3/changes"] = """
            {"newStartPageToken":"9","changes":[
              {"fileId":"mine","removed":false,"file":{"id":"mine","name":"a.jpg","mimeType":"image/jpeg","ownedByMe":true}},
              {"fileId":"theirs","removed":false,"file":{"id":"theirs","name":"b.jpg","mimeType":"image/jpeg","ownedByMe":false}}]}
            """;

        var page = await Client(google).ListChangesAsync(RefreshToken, "8");

        Assert.Equal(["mine"], page.Changes.Select(c => c.FileId));
    }

    [Fact]
    public async Task A_token_error_that_is_not_a_revoked_grant_is_a_transient_failure_and_not_an_authorization_one()
    {
        var google = new FakeGoogle { RefreshStatus = HttpStatusCode.ServiceUnavailable, RefreshBody = """{"error":"temporarily_unavailable"}""" };

        var failure = await Record.ExceptionAsync(() => Client(google).GetStartPageTokenAsync(RefreshToken));

        Assert.IsType<HttpRequestException>(failure);
        Assert.DoesNotContain("refresh-token", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_the_changes_as_files_that_are_there_and_files_that_are_gone_and_ignores_the_rest()
    {
        var google = new FakeGoogle();
        google.Json["/drive/v3/changes"] = """
            {"newStartPageToken":"43","changes":[
              {"fileId":"photo","removed":false,"file":{"id":"photo","name":"a.jpg","mimeType":"image/jpeg","modifiedTime":"2020-05-01T10:00:00.000Z"}},
              {"fileId":"deleted","removed":true},
              {"fileId":"trashed","removed":false,"file":{"id":"trashed","name":"b.jpg","mimeType":"image/jpeg","trashed":true}},
              {"fileId":"folder","removed":false,"file":{"id":"folder","name":"Fotos","mimeType":"application/vnd.google-apps.folder"}},
              {"fileId":"doc","removed":false,"file":{"id":"doc","name":"notes","mimeType":"application/pdf"}}]}
            """;

        var page = await Client(google).ListChangesAsync(RefreshToken, "42");

        Assert.Null(page.NextPageToken);
        Assert.Equal("43", page.NewStartPageToken);
        Assert.Equal([("photo", false), ("deleted", true), ("trashed", true)], page.Changes.Select(c => (c.FileId, c.Removed)));
        Assert.Equal("a.jpg", page.Changes[0].File!.Name);
        Assert.Null(page.Changes[1].File);
        var query = google.Requests.Single(r => r.Path == "/drive/v3/changes").Query;
        Assert.Equal("42", query["pageToken"]);
        // The client library leaves out a parameter that has its default (includeRemoved is true by default).
        Assert.NotEqual("false", query["includeRemoved"]);
        Assert.Equal("true", query["restrictToMyDrive"]);
    }

    [Fact]
    public async Task Reads_a_folders_name_and_parent_and_a_missing_folder_as_null()
    {
        var google = new FakeGoogle();
        google.Json["/drive/v3/files/f1"] = """{"name":"Fotos","parents":["root-id"]}""";

        var folder = await Client(google).GetFolderAsync(RefreshToken, "f1");
        var missing = await Client(google).GetFolderAsync(RefreshToken, "nope");

        Assert.Equal(new DriveFolder("Fotos", "root-id"), folder);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Writes_the_content_of_a_file_into_the_stream_it_is_given()
    {
        var google = new FakeGoogle();
        google.Bytes["/drive/v3/files/a"] = Encoding.UTF8.GetBytes("the picture");
        using var destination = new MemoryStream();

        await Client(google).DownloadAsync(RefreshToken, "a", destination);

        Assert.Equal("the picture", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Equal("media", google.Requests.Last(r => r.Path == "/drive/v3/files/a").Query["alt"]);
    }

    [Fact]
    public async Task A_download_google_refuses_fails_without_the_token_or_the_address_in_the_message()
    {
        var google = new FakeGoogle();

        var failure = await Record.ExceptionAsync(() => Client(google).DownloadAsync(RefreshToken, "gone", new MemoryStream()));

        Assert.NotNull(failure);
        Assert.DoesNotContain("refresh-token", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("googleapis", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_google_refuses_is_an_authorization_failure()
    {
        var google = new FakeGoogle { RefreshStatus = HttpStatusCode.BadRequest, RefreshBody = """{"error":"invalid_grant"}""" };

        await Assert.ThrowsAsync<CloudAuthorizationException>(() => Client(google).ListMediaAsync(RefreshToken, null));
        await Assert.ThrowsAsync<CloudAuthorizationException>(() => Client(google).GetStartPageTokenAsync(RefreshToken));
        Assert.DoesNotContain(google.Requests, r => r.Path.StartsWith("/drive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refreshes_the_access_token_with_the_refresh_token_in_a_body_and_none_in_an_address()
    {
        var google = new FakeGoogle();
        google.Json["/drive/v3/changes/startPageToken"] = """{"startPageToken":"1"}""";

        await Client(google).GetStartPageTokenAsync(RefreshToken);

        Assert.All(google.Requests, r => Assert.DoesNotContain("refresh-token", r.Uri.AbsoluteUri, StringComparison.Ordinal));
        Assert.Equal(RefreshToken, HttpUtility.ParseQueryString(google.RefreshBodies.Single())["refresh_token"]);
    }

    private static GoogleDriveSyncClient Client(FakeGoogle google) => new("client-id", "client-secret", google);

    private sealed record Seen(Uri Uri, string Path, System.Collections.Specialized.NameValueCollection Query);

    private sealed class FakeGoogle : HttpMessageHandler
    {
        public Dictionary<string, string> Json { get; } = [];

        public Dictionary<string, byte[]> Bytes { get; } = [];

        public List<Seen> Requests { get; } = [];

        public List<string> RefreshBodies { get; } = [];

        public HttpStatusCode RefreshStatus { get; init; } = HttpStatusCode.OK;

        public string RefreshBody { get; init; } = """{"access_token":"ya29.access","token_type":"Bearer","expires_in":3600}""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "oauth2.googleapis.com")
            {
                RefreshBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(RefreshStatus) { Content = new StringContent(RefreshBody, Encoding.UTF8, "application/json") };
            }

            var path = uri.AbsolutePath;
            Requests.Add(new Seen(uri, path, HttpUtility.ParseQueryString(uri.Query)));
            if (Bytes.TryGetValue(path, out var bytes) && HttpUtility.ParseQueryString(uri.Query)["alt"] == "media")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }

            return Json.TryGetValue(path, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":{"code":404,"message":"File not found."}}""", Encoding.UTF8, "application/json") };
        }
    }
}
