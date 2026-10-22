using System.Net;
using System.Text;
using Sepiabox.Catalog;
using Sepiabox.Worker.Sources;

namespace Sepiabox.Worker.Tests.Sources;

/// <summary><see cref="GraphOneDriveSyncClient"/> against a fake in place of Microsoft (S-039): the delta pages, and what it does when Graph throttles or says to start again.</summary>
public sealed class GraphOneDriveSyncClientTests
{
    private const string RefreshToken = "M.refresh-token-never-in-a-message";
    private const string DeltaUri = "https://graph.microsoft.com/v1.0/me/drive/root/delta";

    [Fact]
    public async Task Reads_a_delta_page_as_photos_and_videos_that_are_there_and_files_that_are_gone_and_ignores_the_rest()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.OK, """
            {"value":[
              {"id":"photo","name":"Praia.jpg","size":1234,"lastModifiedDateTime":"2021-06-02T11:00:00Z","webUrl":"https://onedrive.live.com/?id=photo",
               "parentReference":{"id":"folder-1"},"file":{"mimeType":"image/jpeg","hashes":{"sha1Hash":"abc"}}},
              {"id":"gone","deleted":{}},
              {"id":"folder","name":"Fotos","folder":{"childCount":2}},
              {"id":"doc","name":"notes.pdf","file":{"mimeType":"application/pdf"}}],
             "@odata.nextLink":"https://graph.microsoft.com/v1.0/me/drive/root/delta?token=2"}
            """);

        var page = await Client(graph).DeltaAsync("access", null);

        Assert.Equal("https://graph.microsoft.com/v1.0/me/drive/root/delta?token=2", page.NextLink);
        Assert.Null(page.DeltaLink);
        Assert.Equal([("photo", false), ("gone", true)], page.Changes.Select(c => (c.Id, c.Removed)));
        var file = page.Changes[0].File!;
        Assert.Equal(("Praia.jpg", "image/jpeg", 1234L, "abc", "folder-1"), (file.Name, file.MimeType, file.Size, file.Hash, file.ParentId));
        Assert.Equal(new DateTimeOffset(2021, 6, 2, 11, 0, 0, TimeSpan.Zero), file.ModifiedTime);
        Assert.Contains("$top=200", graph.Requests.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_last_page_gives_the_delta_link_and_no_next_link()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.OK, """{"value":[],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/me/drive/root/delta?token=done"}""");

        var page = await Client(graph).DeltaAsync("access", $"{DeltaUri}?token=1");

        Assert.Equal((null, "https://graph.microsoft.com/v1.0/me/drive/root/delta?token=done"), (page.NextLink, page.DeltaLink));
    }

    [Fact]
    public async Task A_410_on_a_delta_link_asks_for_the_delta_to_start_again()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.Gone, """{"error":{"code":"resyncRequired"}}""");

        await Assert.ThrowsAsync<DeltaResyncRequiredException>(() => Client(graph).DeltaAsync("access", $"{DeltaUri}?token=old"));
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task A_429_is_asked_again_and_the_run_goes_on_when_graph_stops_throttling()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.TooManyRequests, "{}", retryAfter: 7);
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.TooManyRequests, "{}", retryAfter: 7);
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.OK, """{"value":[],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/me/drive/root/delta?token=d"}""");

        var page = await Client(graph).DeltaAsync("access", null);

        Assert.NotNull(page.DeltaLink);
        Assert.Equal(3, graph.Requests.Count);
    }

    [Fact]
    public async Task A_429_that_never_stops_fails_after_a_few_attempts_without_the_token_in_the_message()
    {
        var graph = new FakeGraph { Default = (HttpStatusCode.TooManyRequests, "{}", 0, null) };

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => Client(graph).DeltaAsync("access-token-secret", null));

        Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
        Assert.Equal(4, graph.Requests.Count);
        Assert.DoesNotContain("secret", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_retry_after_of_more_than_five_minutes_ends_the_call_at_once_and_one_of_a_few_seconds_is_waited()
    {
        var long_ = new FakeGraph();
        long_.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.TooManyRequests, "{}", retryAfter: 301);
        var short_ = new FakeGraph();
        short_.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.TooManyRequests, "{}", retryAfter: 0);
        short_.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.OK, """{"value":[]}""");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => new GraphOneDriveSyncClient("id", "secret", long_).DeltaAsync("access", null));
        await new GraphOneDriveSyncClient("id", "secret", short_, TimeSpan.Zero).DeltaAsync("access", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
        Assert.Single(long_.Requests);
        Assert.Equal(2, short_.Requests.Count);
    }

    [Fact]
    public async Task A_404_on_a_delta_link_asks_for_the_delta_to_start_again_and_on_the_first_call_is_a_failure()
    {
        var graph = new FakeGraph();

        await Assert.ThrowsAsync<DeltaResyncRequiredException>(() => Client(graph).DeltaAsync("access", $"{DeltaUri}?token=gone"));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => Client(graph).DeltaAsync("access", null));
        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
    }

    [Fact]
    public async Task A_401_is_asked_once_more_before_it_is_an_authorization_failure()
    {
        var recovers = new FakeGraph();
        recovers.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.Unauthorized, "{}");
        recovers.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.OK, """{"value":[]}""");
        var stays = new FakeGraph();
        stays.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.Unauthorized, "{}");

        await Client(recovers).DeltaAsync("access", null);
        await Assert.ThrowsAsync<CloudAuthorizationException>(() => Client(stays).DeltaAsync("access", null));

        Assert.Equal(2, recovers.Requests.Count);
        Assert.Equal(2, stays.Requests.Count);
    }

    [Fact]
    public async Task Keeps_files_of_an_unknown_type_for_the_sync_to_decide_by_name_and_leaves_out_documents()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.OK, """
            {"value":[
              {"id":"raw","name":"IMG_1.CR2","file":{"mimeType":"application/octet-stream"}},
              {"id":"none","name":"clip.mov","file":{}},
              {"id":"doc","name":"a.pdf","file":{"mimeType":"application/pdf"}}]}
            """);

        var page = await Client(graph).DeltaAsync("access", null);

        Assert.Equal(["raw", "none"], page.Changes.Select(c => c.Id));
    }

    [Fact]
    public async Task An_invalid_client_is_a_failure_and_not_a_reason_to_connect_again()
    {
        var graph = new FakeGraph { TokenStatus = HttpStatusCode.Unauthorized, TokenBody = """{"error":"invalid_client"}""" };

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(graph).RefreshAsync(RefreshToken));
    }

    [Theory]
    [InlineData("https://abc123.files.1drv.com/y4mOpaqueCredentialSegment/photo.jpg", false)]
    [InlineData("https://onedrive.live.com/download?psid=1&cid=abc", false)]
    [InlineData("https://contoso-my.sharepoint.com/personal/x/_layouts/15/download.aspx?UniqueId=1", false)]
    [InlineData("https://lh3.googleusercontent.com/d/abc", false)]
    [InlineData("https://1drv.com/opaque", false)]
    [InlineData("https://graph.microsoft.com/v1.0/me/drive/root/delta", true)]
    [InlineData("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", true)]
    [InlineData("https://www.googleapis.com/drive/v3/files/a?alt=media", true)]
    [InlineData("https://oauth2.googleapis.com/token", true)]
    [InlineData("https://files.example.com/a?tempauth=secret&x=1", false)]
    [InlineData("https://files.example.com/a?x=1&tempauth=secret", false)]
    [InlineData("https://graph.microsoft.com/v1.0/me/drive/root/delta?token=2", true)]
    [InlineData("https://files.example.com/a", true)]
    public void The_tracing_leaves_out_a_download_host_and_a_request_whose_address_carries_a_credential_and_keeps_the_api_hosts(string uri, bool traced)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        Assert.Equal(traced, Microsoft.Extensions.Hosting.TelemetryFilter.IsTraced(request));
    }

    [Fact]
    public async Task Asks_the_consumers_tenant_for_exactly_the_scopes_the_connection_was_given()
    {
        var graph = new FakeGraph();

        await Client(graph).RefreshAsync(RefreshToken);

        Assert.Equal("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", Assert.Single(graph.TokenUris));
        var form = System.Web.HttpUtility.ParseQueryString(graph.TokenForms.Single());
        // Read-only files, a refresh token and the account's address: a wider scope (Files.ReadWrite) would be a different promise to the family.
        Assert.Equal("Files.Read offline_access User.Read", form["scope"]);
        Assert.Equal("Files.Read offline_access User.Read", GraphOneDriveSyncClient.Scope);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(600)]
    public async Task A_retry_after_of_more_than_five_minutes_ends_the_call_at_once(int seconds)
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.TooManyRequests, "{}", retryAfter: seconds);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => new GraphOneDriveSyncClient("id", "secret", graph).DeltaAsync("access", null));

        Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task Reads_a_retry_after_given_as_a_date_and_ends_the_call_when_it_is_far_off()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.ServiceUnavailable, "{}", retryDate: DateTimeOffset.UtcNow.AddMinutes(20));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => new GraphOneDriveSyncClient("id", "secret", graph).DeltaAsync("access", null));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task Prefers_the_sha1_hash_to_the_quick_xor_one_and_takes_the_quick_xor_when_it_is_all_there_is()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.OK, """
            {"value":[
              {"id":"both","name":"a.jpg","file":{"mimeType":"image/jpeg","hashes":{"quickXorHash":"QX","sha1Hash":"SHA1"}}},
              {"id":"xor","name":"b.jpg","file":{"mimeType":"image/jpeg","hashes":{"quickXorHash":"QX"}}},
              {"id":"none","name":"c.jpg","file":{"mimeType":"image/jpeg"}}]}
            """);

        var page = await Client(graph).DeltaAsync("access", null);

        Assert.Equal(["SHA1", "QX", null], page.Changes.Select(c => c.File!.Hash));
    }

    [Theory]
    [InlineData("http://files.example.com/a")]
    [InlineData("ftp://files.example.com/a")]
    [InlineData("not an address")]
    public async Task Never_downloads_from_an_address_that_is_not_https(string address)
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/items/a", HttpStatusCode.OK, "{\"@microsoft.graph.downloadUrl\":\"" + address + "\"}");
        graph.Downloads["/a"] = [1];

        await Assert.ThrowsAsync<IOException>(() => Client(graph).DownloadAsync("access", "a", new MemoryStream()));

        Assert.Null(graph.DownloadAuthorization);
    }

    [Fact]
    public async Task Never_follows_a_next_link_that_is_not_microsofts_graph()
    {
        var graph = new FakeGraph();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(graph).DeltaAsync("access", "https://evil.example.com/delta"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(graph).DeltaAsync("access", "http://graph.microsoft.com/v1.0/me/drive/root/delta"));

        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task A_refused_access_token_is_an_authorization_failure()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/root/delta", HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<CloudAuthorizationException>(() => Client(graph).DeltaAsync("access", null));
    }

    [Fact]
    public async Task A_refresh_gives_the_rolled_token_and_a_revoked_grant_is_an_authorization_failure_and_any_other_error_is_not()
    {
        var rolled = new FakeGraph { TokenBody = """{"access_token":"graph-access","refresh_token":"M.rolled"}""" };
        var refused = new FakeGraph { TokenStatus = HttpStatusCode.BadRequest, TokenBody = """{"error":"invalid_grant"}""" };
        var down = new FakeGraph { TokenStatus = HttpStatusCode.ServiceUnavailable, TokenBody = """{"error":"temporarily_unavailable"}""" };

        Assert.Equal(new OneDriveToken("graph-access", "M.rolled"), await Client(rolled).RefreshAsync(RefreshToken));
        Assert.Equal(RefreshToken, System.Web.HttpUtility.ParseQueryString(rolled.TokenForms.Single())["refresh_token"]);
        await Assert.ThrowsAsync<CloudAuthorizationException>(() => Client(refused).RefreshAsync(RefreshToken));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => Client(down).RefreshAsync(RefreshToken));
        Assert.DoesNotContain("refresh-token", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_a_folders_name_and_parent_the_top_of_the_drive_as_no_parent_and_a_missing_folder_as_null()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/items/f1", HttpStatusCode.OK, """{"id":"f1","name":"Fotos","parentReference":{"id":"root-id"}}""");
        graph.Respond("/v1.0/me/drive/items/root-id", HttpStatusCode.OK, """{"id":"root-id","name":"root","root":{},"parentReference":{"id":"x"}}""");

        Assert.Equal(new OneDriveFolder("Fotos", "root-id"), await Client(graph).GetFolderAsync("access", "f1"));
        Assert.Null((await Client(graph).GetFolderAsync("access", "root-id"))!.ParentId);
        Assert.Null(await Client(graph).GetFolderAsync("access", "nope"));
    }

    [Fact]
    public async Task Downloads_from_the_address_graph_gives_without_sending_the_access_token_to_it()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/items/a", HttpStatusCode.OK, """{"@microsoft.graph.downloadUrl":"https://files.example.com/a"}""");
        graph.Downloads["/a"] = Encoding.UTF8.GetBytes("the picture");
        using var destination = new MemoryStream();

        await Client(graph).DownloadAsync("access", "a", destination);

        Assert.Equal("the picture", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Null(graph.DownloadAuthorization);
    }

    [Fact]
    public async Task A_download_that_fails_is_an_io_failure_without_the_address_in_the_message()
    {
        var graph = new FakeGraph();
        graph.Respond("/v1.0/me/drive/items/a", HttpStatusCode.OK, """{"@microsoft.graph.downloadUrl":"https://files.example.com/a"}""");

        var failure = await Assert.ThrowsAsync<IOException>(() => Client(graph).DownloadAsync("access", "a", new MemoryStream()));

        Assert.DoesNotContain("files.example.com", failure.Message, StringComparison.Ordinal);
    }

    private static GraphOneDriveSyncClient Client(FakeGraph graph) => new("client-id", "client-secret", graph, TimeSpan.Zero);

    private sealed class FakeGraph : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body, int? RetryAfter, DateTimeOffset? RetryDate)>> _responses = [];

        public (HttpStatusCode Status, string Body, int? RetryAfter, DateTimeOffset? RetryDate)? Default { get; init; }

        public HttpStatusCode TokenStatus { get; init; } = HttpStatusCode.OK;

        public string TokenBody { get; init; } = """{"access_token":"graph-access"}""";

        public List<string> Requests { get; } = [];

        public List<string> TokenForms { get; } = [];

        public List<string> TokenUris { get; } = [];

        public Dictionary<string, byte[]> Downloads { get; } = [];

        public string? DownloadAuthorization { get; private set; }

        public void Respond(string path, HttpStatusCode status, string body, int? retryAfter = null, DateTimeOffset? retryDate = null)
        {
            if (!_responses.TryGetValue(path, out var queue))
            {
                _responses[path] = queue = new Queue<(HttpStatusCode, string, int?, DateTimeOffset?)>();
            }

            queue.Enqueue((status, body, retryAfter, retryDate));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "login.microsoftonline.com")
            {
                TokenForms.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                TokenUris.Add(uri.AbsoluteUri);
                return Json(TokenStatus, TokenBody, null, null);
            }

            if (uri.Host == "files.example.com")
            {
                DownloadAuthorization = request.Headers.Authorization?.ToString();
                return Downloads.TryGetValue(uri.AbsolutePath, out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            Requests.Add(uri.AbsoluteUri);
            if (_responses.TryGetValue(uri.AbsolutePath, out var queue) && queue.Count > 0)
            {
                var (status, body, retryAfter, retryDate) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                return Json(status, body, retryAfter, retryDate);
            }

            return Default is { } fallback ? Json(fallback.Status, fallback.Body, fallback.RetryAfter, fallback.RetryDate) : Json(HttpStatusCode.NotFound, "{}", null, null);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body, int? retryAfter, DateTimeOffset? retryDate)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (retryAfter is { } seconds)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }
            else if (retryDate is { } date)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(date);
            }

            return response;
        }
    }
}
