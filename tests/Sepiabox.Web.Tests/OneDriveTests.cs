using System.Net;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Web.Auth;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Sources;

namespace Sepiabox.Web.Tests;

/// <summary><see cref="MicrosoftOneDrive"/> against a fake in place of Microsoft (S-039, D-116): what it asks for and how it reads the answer.</summary>
public sealed class MicrosoftOneDriveHttpTests
{
    private const string RefreshToken = "M.refresh-token-never-in-an-address";

    [Fact]
    public void Asks_for_the_files_a_refresh_token_and_the_address_for_the_consumers_tenant_without_the_secret()
    {
        var uri = new MicrosoftOneDrive(new OAuthClient("client-id", "never-sent")).AuthorizationUri("https://app.example.com/sources/onedrive/callback", "the state");
        var query = HttpUtility.ParseQueryString(uri.Query);

        Assert.Equal("login.microsoftonline.com", uri.Host);
        Assert.Contains("/consumers/", uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("Files.Read offline_access User.Read", query["scope"]);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("the state", query["state"]);
        Assert.DoesNotContain("never-sent", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_not_configured_without_its_own_client_and_cannot_make_an_address()
    {
        var onedrive = new MicrosoftOneDrive(null);

        Assert.False(onedrive.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => onedrive.AuthorizationUri("https://app.example.com/cb", "state"));
    }

    [Fact]
    public async Task Exchanges_a_code_for_the_refresh_token_and_sends_the_secret_in_the_body_only()
    {
        var api = new FakeMicrosoft();

        var token = await Client(api).ExchangeCodeAsync("the-code", "https://app.example.com/cb");

        Assert.Equal("M.rolled", token);
        var form = HttpUtility.ParseQueryString(api.TokenBodies.Single());
        Assert.Equal(("authorization_code", "the-code", "client-secret"), (form["grant_type"], form["code"], form["client_secret"]));
        Assert.All(api.Uris, u => Assert.DoesNotContain("client-secret", u, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refresh_gives_the_access_token_and_the_refresh_token_it_rolled_to_with_none_in_an_address()
    {
        var api = new FakeMicrosoft();

        var token = await Client(api).RefreshAsync(RefreshToken);

        Assert.Equal(new OneDriveWebToken("graph-access", "M.rolled"), token);
        Assert.Equal(RefreshToken, HttpUtility.ParseQueryString(api.TokenBodies.Single())["refresh_token"]);
        Assert.All(api.Uris, u => Assert.DoesNotContain("refresh-token", u, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_grant_microsoft_refuses_is_an_authorization_failure_and_any_other_error_is_not()
    {
        var refused = new FakeMicrosoft { TokenStatus = HttpStatusCode.BadRequest, TokenBody = """{"error":"invalid_grant"}""" };
        var down = new FakeMicrosoft { TokenStatus = HttpStatusCode.ServiceUnavailable, TokenBody = """{"error":"temporarily_unavailable"}""" };

        await Assert.ThrowsAsync<CloudAuthorizationException>(() => Client(refused).RefreshAsync(RefreshToken));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => Client(down).RefreshAsync(RefreshToken));
        Assert.DoesNotContain("refresh-token", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_whose_onedrive_it_is_from_the_mail_or_the_user_name_lower_cased()
    {
        var api = new FakeMicrosoft();
        api.Graph["/v1.0/me"] = """{"mail":null,"userPrincipalName":"Ana@Outlook.com"}""";

        Assert.Equal("ana@outlook.com", await Client(api).GetAccountEmailAsync("graph-access"));
    }

    [Fact]
    public async Task Counts_images_and_videos_across_the_delta_pages_and_leaves_out_what_is_deleted_or_not_a_file()
    {
        var api = new FakeMicrosoft();
        api.Graph["/v1.0/me/drive/root/delta"] = """
            {"value":[{"id":"a","file":{"mimeType":"image/jpeg"}},{"id":"b","file":{"mimeType":"video/mp4"}},{"id":"c"}],
             "@odata.nextLink":"https://graph.microsoft.com/v1.0/me/drive/root/delta?token=2"}
            """;
        api.Graph["/v1.0/me/drive/root/delta?token=2"] = """
            {"value":[{"id":"d","file":{"mimeType":"image/png"}},{"id":"e","deleted":{},"file":{"mimeType":"image/png"}},{"id":"f","file":{"mimeType":"application/pdf"}}]}
            """;

        Assert.Equal(new DriveMediaCount(2, 1), await Client(api).CountMediaAsync("graph-access"));
    }

    [Fact]
    public async Task Does_not_follow_a_next_link_that_is_not_microsofts_graph()
    {
        var api = new FakeMicrosoft();
        api.Graph["/v1.0/me/drive/root/delta"] = """{"value":[],"@odata.nextLink":"https://evil.example.com/steal"}""";

        await Client(api).CountMediaAsync("graph-access");

        Assert.DoesNotContain(api.Uris, u => u.Contains("evil.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Opens_an_item_through_its_download_address_and_an_item_that_is_gone_as_null()
    {
        var api = new FakeMicrosoft();
        api.Graph["/v1.0/me/drive/items/item-1"] = """{"name":"Praia.jpg","size":3,"file":{"mimeType":"image/jpeg"},"@microsoft.graph.downloadUrl":"https://files.example.com/item-1"}""";
        api.Downloads["/item-1"] = [1, 2, 3];

        await using var opened = await Client(api).OpenFileAsync("graph-access", "item-1");
        var gone = await Client(api).OpenFileAsync("graph-access", "nope");

        Assert.Equal(("Praia.jpg", "image/jpeg", 3L), (opened!.Name, opened.ContentType, opened.Length));
        using var copy = new MemoryStream();
        await opened.Content.CopyToAsync(copy);
        Assert.Equal([1, 2, 3], copy.ToArray());
        Assert.Null(gone);
        // The download address is pre-authenticated: the access token is not sent to it.
        Assert.DoesNotContain("graph-access", api.DownloadAuthorizations.Single() ?? string.Empty, StringComparison.Ordinal);
    }

    private static MicrosoftOneDrive Client(FakeMicrosoft api) => new(new OAuthClient("client-id", "client-secret"), api);

    private sealed class FakeMicrosoft : HttpMessageHandler
    {
        public HttpStatusCode TokenStatus { get; init; } = HttpStatusCode.OK;

        public string TokenBody { get; init; } = """{"access_token":"graph-access","refresh_token":"M.rolled","expires_in":3600}""";

        public Dictionary<string, string> Graph { get; } = [];

        public Dictionary<string, byte[]> Downloads { get; } = [];

        public List<string> Uris { get; } = [];

        public List<string> TokenBodies { get; } = [];

        public List<string?> DownloadAuthorizations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Uris.Add(uri.AbsoluteUri);
            if (uri.Host == "login.microsoftonline.com")
            {
                TokenBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json(TokenStatus, TokenBody);
            }

            if (uri.Host == "files.example.com")
            {
                DownloadAuthorizations.Add(request.Headers.Authorization?.ToString());
                return Downloads.TryGetValue(uri.AbsolutePath, out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var key = Graph.ContainsKey(uri.AbsolutePath + uri.Query) ? uri.AbsolutePath + uri.Query : uri.AbsolutePath;
            // A query that selects fields is not part of the key; a delta link's token is.
            if (!Graph.ContainsKey(key) && uri.Query.Contains("token=", StringComparison.Ordinal))
            {
                key = uri.AbsolutePath + "?token=" + HttpUtility.ParseQueryString(uri.Query)["token"];
            }

            return Graph.TryGetValue(key, out var json) ? Json(HttpStatusCode.OK, json) : Json(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

/// <summary>OneDrive connections on the catalog (S-039): the rolled refresh token is the one kept.</summary>
[Collection(CatalogGroup.Name)]
public sealed class OneDriveConnectionTests(CatalogFixture catalog)
{
    private const string Redirect = "https://app.example.com/sources/onedrive/callback";
    private static readonly IDataProtectionProvider s_keys = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task Connecting_stores_the_newest_refresh_token_protected_and_the_address_lower_cased()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var onedrive = new FakeOneDrive { RolledTo = "M.rolled-1" };

        var outcome = await Sources(db, onedrive).CompleteOneDriveConnectionAsync("code", Redirect);

        Assert.Equal(SourceOutcome.Done, outcome);
        var row = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.Equal((ConnectedSourceTypes.OneDrive, "ana@outlook.com"), (row.Type, row.AccountEmail));
        Assert.DoesNotContain("rolled", row.EncryptedRefreshToken, StringComparison.Ordinal);
        Assert.Equal("M.rolled-1", new DriveTokenProtector(s_keys).Unprotect(row.EncryptedRefreshToken));
    }

    [Fact]
    public async Task A_refused_code_or_a_failing_refresh_connects_nothing()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var onedrive = new FakeOneDrive { RefreshToken = null };
        var sources = Sources(db, onedrive);

        Assert.Equal(SourceOutcome.Failed, await sources.CompleteOneDriveConnectionAsync("code", Redirect));
        onedrive.RefreshToken = "M.token";
        onedrive.Failure = () => new HttpRequestException("down");
        Assert.Equal(SourceOutcome.Failed, await sources.CompleteOneDriveConnectionAsync("code", Redirect));

        Assert.Empty(await sources.ListAsync());
    }

    [Fact]
    public async Task Counting_keeps_the_token_microsoft_rolled_to_and_uses_it_the_next_time()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var onedrive = new FakeOneDrive();
        var sources = Sources(db, onedrive);
        await sources.CompleteOneDriveConnectionAsync("code", Redirect);
        var id = (await sources.ListAsync()).Single().Id;
        onedrive.RolledTo = "M.second";

        Assert.Equal(SourceOutcome.Done, await sources.CountAsync(id));

        var listed = (await sources.ListAsync()).Single();
        Assert.Equal((5, 1), (listed.PhotoCount, listed.VideoCount));
        Assert.Equal("M.second", new DriveTokenProtector(s_keys).Unprotect((await db.ConnectedSources.AsNoTracking().SingleAsync()).EncryptedRefreshToken));
        onedrive.RolledTo = null;
        await sources.CountAsync(id);
        Assert.Equal(["M.refresh-token-in-clear-text", "M.refresh-token-in-clear-text", "M.second"], onedrive.Refreshed.TakeLast(3));
    }

    [Fact]
    public async Task A_token_microsoft_refuses_needs_reconnecting_and_another_failure_leaves_the_account_alone()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var onedrive = new FakeOneDrive();
        var sources = Sources(db, onedrive);
        await sources.CompleteOneDriveConnectionAsync("code", Redirect);
        var id = (await sources.ListAsync()).Single().Id;

        onedrive.Failure = () => new HttpRequestException("down");
        Assert.Equal(SourceOutcome.Failed, await sources.CountAsync(id));
        Assert.Equal(ConnectedSourceStatuses.Connected, (await sources.ListAsync()).Single().Status);

        onedrive.Failure = () => new CloudAuthorizationException("invalid_grant");
        Assert.Equal(SourceOutcome.Reconnect, await sources.CountAsync(id));
        Assert.True((await sources.ListAsync()).Single().NeedsReconnecting);
    }

    [Fact]
    public async Task Disconnecting_deletes_the_connection_and_does_not_ask_google_or_anyone_to_forget_it()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        var sources = Sources(db, new FakeOneDrive(), drive);
        await sources.CompleteOneDriveConnectionAsync("code", Redirect);

        Assert.Equal(SourceOutcome.Done, await sources.DisconnectAsync((await sources.ListAsync()).Single().Id));

        Assert.Empty(await sources.ListAsync());
        Assert.Empty(drive.Revoked);
    }

    [Fact]
    public async Task The_same_address_in_google_drive_and_onedrive_is_two_connections()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive { Email = "ana@outlook.com" };
        var sources = Sources(db, new FakeOneDrive(), drive);

        await sources.CompleteGoogleDriveConnectionAsync("code", "https://app.example.com/sources/google-drive/callback");
        await sources.CompleteOneDriveConnectionAsync("code", Redirect);

        Assert.Equal([ConnectedSourceTypes.GoogleDrive, ConnectedSourceTypes.OneDrive], (await sources.ListAsync()).Select(s => s.Type).Order());
    }

    private ConnectedSources Sources(CatalogDbContext db, FakeOneDrive onedrive, FakeDrive? drive = null)
    {
        drive ??= new FakeDrive();
        return new ConnectedSources(
            new TestContexts(catalog.ConnectionStringFor(db)), new DriveTokenProtector(s_keys), drive, drive, onedrive, onedrive, TimeProvider.System, NullLogger<ConnectedSources>.Instance);
    }
}

/// <summary>"Open original" for a OneDrive source (S-039): the rolled token is kept, a refused one asks to reconnect.</summary>
[Collection(CatalogGroup.Name)]
public sealed class OneDriveOriginalTests(CatalogFixture catalog)
{
    private const string Account = "ana@outlook.com";
    private static readonly IDataProtectionProvider s_keys = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task Opens_the_item_the_sync_recorded_and_keeps_the_refresh_token_microsoft_rolled_to()
    {
        await using var db = await SeedAsync();
        var onedrive = new FakeOneDrive { RolledTo = "M.second" };
        onedrive.Files["item-1"] = Encoding.UTF8.GetBytes("jpeg bytes");

        var (outcome, file) = await Originals(db, onedrive).OpenAsync(Ids.Of(1), await db.AssetSources.Select(s => s.Id).SingleAsync());

        Assert.Equal(DriveOriginalOutcome.Opened, outcome);
        await using (file)
        {
            using var reader = new StreamReader(file!.Content);
            Assert.Equal("jpeg bytes", await reader.ReadToEndAsync());
        }

        Assert.Equal(["M.first"], onedrive.Refreshed);
        Assert.Equal("M.second", new DriveTokenProtector(s_keys).Unprotect((await db.ConnectedSources.AsNoTracking().SingleAsync()).EncryptedRefreshToken));
    }

    [Fact]
    public async Task A_refused_token_makes_the_account_reconnect_and_a_missing_item_is_not_found()
    {
        await using var db = await SeedAsync();
        var onedrive = new FakeOneDrive();
        var source = await db.AssetSources.Select(s => s.Id).SingleAsync();

        Assert.Equal(DriveOriginalOutcome.NotFound, (await Originals(db, onedrive).OpenAsync(Ids.Of(1), source)).Outcome);
        onedrive.Failure = () => new CloudAuthorizationException("invalid_grant");
        Assert.Equal(DriveOriginalOutcome.Reconnect, (await Originals(db, onedrive).OpenAsync(Ids.Of(1), source)).Outcome);
        Assert.Equal(ConnectedSourceStatuses.Reconnect, (await db.ConnectedSources.AsNoTracking().SingleAsync()).Status);
    }

    private DriveOriginals Originals(CatalogDbContext db, FakeOneDrive onedrive) =>
        new(new TestContexts(catalog.ConnectionStringFor(db)), new DriveTokenProtector(s_keys), new FakeDrive(), onedrive, NullLogger<DriveOriginals>.Instance);

    private async Task<CatalogDbContext> SeedAsync()
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        var asset = CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 9, 0, 0));
        asset.Sources = [new AssetSource { AssetId = Ids.Of(1), Type = "onedrive", Name = Account, Path = "Fotos/Praia.jpg", ModifiedAt = DateTimeOffset.UtcNow }];
        db.Assets.Add(asset);
        var account = new ConnectedSource
        {
            Type = ConnectedSourceTypes.OneDrive,
            AccountEmail = Account,
            EncryptedRefreshToken = new DriveTokenProtector(s_keys).Protect("M.first"),
            Status = ConnectedSourceStatuses.Connected,
            ConnectedAt = DateTimeOffset.UtcNow,
        };
        db.ConnectedSources.Add(account);
        await db.SaveChangesAsync();
        db.DriveFiles.Add(new DriveFile { ConnectedSourceId = account.Id, FileId = "item-1", Path = "Fotos/Praia.jpg", ModifiedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}

/// <summary>Connecting OneDrive (S-039): its own addresses, state cookie and state, apart from Google Drive's.</summary>
public sealed class OneDriveEndpointTests
{
    [Fact]
    public async Task Sends_the_browser_to_microsoft_with_a_state_and_a_cookie_scoped_to_onedrive()
    {
        await using var web = Web(new FakeConnectedSources());
        using var http = web.CreateSignedInClient(followRedirects: false);

        var response = await http.GetAsync(new Uri(SourceEndpoints.OneDriveConnectPath, UriKind.Relative));

        Assert.Equal("login.example.com", response.Headers.Location!.Host);
        var query = HttpUtility.ParseQueryString(response.Headers.Location.Query);
        Assert.Equal("http://localhost/sources/onedrive/callback", query["redirect_uri"]);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(SourceEndpoints.OneDriveStateCookie + "=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/sources/onedrive", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Says_it_is_not_set_up_when_there_is_no_onedrive_client()
    {
        await using var web = Web(new FakeConnectedSources { CanConnectOneDrive = false });
        using var http = web.CreateSignedInClient(followRedirects: false);

        var response = await http.GetAsync(new Uri(SourceEndpoints.OneDriveConnectPath, UriKind.Relative));

        Assert.Equal("/sources?note=onedrive-not-configured", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Completes_the_connection_and_says_so_about_onedrive()
    {
        var sources = new FakeConnectedSources();
        await using var web = Web(sources);
        using var http = web.CreateSignedInClient(followRedirects: false);
        var state = await StartAsync(http);

        var response = await http.GetAsync(new Uri($"{SourceEndpoints.OneDriveCallbackPath}?code=the-code&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        Assert.Equal("/sources?note=onedrive-connected", response.Headers.Location!.OriginalString);
        Assert.Equal([("the-code", "http://localhost/sources/onedrive/callback")], sources.CompletedOneDrive);
        Assert.Empty(sources.Completed);
    }

    [Fact]
    public async Task Reports_a_denial_and_a_failure_about_onedrive()
    {
        await using var web = Web(new FakeConnectedSources { Completes = SourceOutcome.Failed });
        using var http = web.CreateSignedInClient(followRedirects: false);
        var state = await StartAsync(http);

        var denied = await http.GetAsync(new Uri($"{SourceEndpoints.OneDriveCallbackPath}?error=access_denied&state={Uri.EscapeDataString(state)}", UriKind.Relative));
        // The callback deleted the state cookie, so a second connection starts again.
        state = await StartAsync(http);
        var failed = await http.GetAsync(new Uri($"{SourceEndpoints.OneDriveCallbackPath}?code=c&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        Assert.Equal("/sources?note=onedrive-denied", denied.Headers.Location!.OriginalString);
        Assert.Equal("/sources?note=onedrive-failed", failed.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_state_made_for_google_drive_is_no_good_for_onedrive_and_the_reverse()
    {
        var sources = new FakeConnectedSources();
        await using var web = Web(sources);
        using var http = web.CreateSignedInClient(followRedirects: false);
        var google = await http.GetAsync(new Uri(SourceEndpoints.ConnectPath, UriKind.Relative));
        var googleState = HttpUtility.ParseQueryString(google.Headers.Location!.Query)["state"]!;
        var nonce = google.Headers.GetValues("Set-Cookie").Single().Split(';')[0][(SourceEndpoints.StateCookie.Length + 1)..];
        using var forged = web.CreateSignedInClient(followRedirects: false);
        forged.DefaultRequestHeaders.Add("Cookie", $"{SourceEndpoints.OneDriveStateCookie}={nonce}");

        var response = await forged.GetAsync(new Uri($"{SourceEndpoints.OneDriveCallbackPath}?code=c&state={Uri.EscapeDataString(googleState)}", UriKind.Relative));

        Assert.Equal("/sources?note=state", response.Headers.Location!.OriginalString);
        Assert.Empty(sources.CompletedOneDrive);
    }

    private static WebFactory Web(FakeConnectedSources sources) => new(new FakeLibrary()) { ConnectedSources = sources };

    private static async Task<string> StartAsync(HttpClient http)
    {
        var started = await http.GetAsync(new Uri(SourceEndpoints.OneDriveConnectPath, UriKind.Relative));
        return HttpUtility.ParseQueryString(started.Headers.Location!.Query)["state"]!;
    }
}

/// <summary>The sources page with OneDrive (S-039): its own section, wording and connect link, in both languages.</summary>
public sealed class OneDriveSourcesPageTests : LocalizedContext
{
    private readonly FakeConnectedSources _sources = new();

    public OneDriveSourcesPageTests()
    {
        Services.AddSingleton<IConnectedSources>(_sources);
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgery());
    }

    [Fact]
    public void Offers_to_connect_onedrive_beside_google_drive_and_lists_each_account_under_its_own_service()
    {
        _sources.Sources.Add(new ConnectedSourceSummary(1, ConnectedSourceTypes.OneDrive, "ana@outlook.com", ConnectedSourceStatuses.Connected, DateTimeOffset.UtcNow, null, 5, 1, DateTimeOffset.UtcNow));

        var page = Render<ConnectedAccounts>();

        Assert.Equal(["Google Drive", "OneDrive"], page.FindAll(".connection-service h2").Select(h => h.TextContent));
        Assert.Equal(["Connect Google Drive", "Connect OneDrive"], page.FindAll("a.connect").Select(a => a.TextContent));
        Assert.Equal("sources/onedrive/connect", page.FindAll("a.connect")[1].GetAttribute("href"));
        Assert.Equal("ana@outlook.com", page.Find("#onedrive .connection-account strong").TextContent);
        Assert.Empty(page.FindAll("#google-drive .connection"));
    }

    [Fact]
    public void Reconnect_for_a_onedrive_account_goes_to_the_onedrive_connect_address()
    {
        _sources.Sources.Add(new ConnectedSourceSummary(1, ConnectedSourceTypes.OneDrive, "ana@outlook.com", ConnectedSourceStatuses.Reconnect, DateTimeOffset.UtcNow, null, null, null, null));

        var page = Render<ConnectedAccounts>();

        Assert.Equal("sources/onedrive/connect", page.Find("#onedrive .connection-actions a.button").GetAttribute("href"));
        Assert.Contains("Microsoft no longer accepts", page.Find("#onedrive .connection .muted").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_onedrive_cannot_be_connected_yet_without_hiding_google_drive()
    {
        using var _ = Cultures.Use(Cultures.BrazilianPortuguese);
        var sources = new FakeConnectedSources { CanConnectOneDrive = false };
        Services.AddSingleton<IConnectedSources>(sources);

        var page = Render<ConnectedAccounts>();

        Assert.Single(page.FindAll("a.connect"));
        Assert.Contains("O OneDrive ainda não foi configurado", page.Find("#onedrive p.note").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("onedrive-connected", "OneDrive connected.")]
    [InlineData("onedrive-denied", "OneDrive was not connected: access was not granted.")]
    [InlineData("onedrive-failed", "OneDrive could not be connected. Try again.")]
    public void Shows_what_happened_from_a_onedrive_note(string note, string text)
    {
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/sources?note={note}");

        Assert.Equal(text, Render<ConnectedAccounts>().Find("p.note").TextContent);
    }

    private sealed class FixedAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
