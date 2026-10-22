using System.Net;
using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Web.Auth;
using Sepiabox.Web.Components.Layout;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Sources;

namespace Sepiabox.Web.Tests;

/// <summary>The refresh token is stored protected, and only as Data Protection can read it back (S-037).</summary>
public sealed class DriveTokenProtectorTests
{
    private const string Token = "1//0g-refresh-token-in-clear-text";

    [Fact]
    public void Protects_a_token_so_that_the_stored_value_does_not_contain_it_and_reads_it_back()
    {
        var protector = new DriveTokenProtector(new EphemeralDataProtectionProvider());

        var stored = protector.Protect(Token);

        Assert.DoesNotContain("refresh-token", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, stored, StringComparison.Ordinal);
        Assert.Equal(Token, protector.Unprotect(stored));
        // Each protection is its own value, so two connections never show they hold the same token.
        Assert.NotEqual(stored, protector.Protect(Token));
    }

    [Fact]
    public void Cannot_read_a_token_protected_by_another_key_ring_or_that_is_damaged()
    {
        var stored = new DriveTokenProtector(new EphemeralDataProtectionProvider()).Protect(Token);
        var other = new DriveTokenProtector(new EphemeralDataProtectionProvider());

        Assert.Null(other.Unprotect(stored));
        Assert.Null(other.Unprotect("not a protected value"));
        Assert.Null(other.Unprotect(string.Empty));
    }

    [Fact]
    public void Uses_a_purpose_of_its_own_so_no_other_protector_of_the_app_can_read_it()
    {
        var keys = new EphemeralDataProtectionProvider();
        var stored = new DriveTokenProtector(keys).Protect(Token);

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => keys.CreateProtector("Something.Else").Unprotect(stored));
        Assert.Equal(Token, keys.CreateProtector(DriveTokenProtector.Purpose).Unprotect(stored));
    }

    [Fact]
    public void Refuses_to_protect_nothing()
    {
        Assert.Throws<ArgumentException>(() => new DriveTokenProtector(new EphemeralDataProtectionProvider()).Protect(string.Empty));
    }
}

/// <summary>Google's consent address (S-037, D-024): the read-only scope, offline access, and consent asked every time.</summary>
public sealed class GoogleDriveAuthorizationTests
{
    [Fact]
    public void Asks_for_the_read_only_scope_with_offline_access_and_consent_for_the_redirect_and_state_given()
    {
        var drive = new GoogleDrive(new OAuthClient("client-id.example.com", "never-sent"));

        var uri = drive.AuthorizationUri("https://app.example.com/sources/google-drive/callback", "the state/with+chars");
        var query = HttpUtility.ParseQueryString(uri.Query);

        Assert.Equal("accounts.google.com", uri.Host);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-id.example.com", query["client_id"]);
        Assert.Equal("https://www.googleapis.com/auth/drive.readonly", query["scope"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("true", query["include_granted_scopes"]);
        Assert.Equal("https://app.example.com/sources/google-drive/callback", query["redirect_uri"]);
        Assert.Equal("the state/with+chars", query["state"]);
        Assert.DoesNotContain("never-sent", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_not_configured_without_the_google_client_and_cannot_make_an_address()
    {
        var drive = new GoogleDrive(null);

        Assert.False(drive.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => drive.AuthorizationUri("https://app.example.com/cb", "state"));
        Assert.True(new GoogleDrive(new OAuthClient("id", "secret")).IsConfigured);
    }
}

/// <summary>The sources page (S-037): accounts, counts, statuses and notes, in both languages.</summary>
public sealed class SourcesPageTests : LocalizedContext
{
    private readonly FakeConnectedSources _sources = new();

    public SourcesPageTests()
    {
        Services.AddSingleton<IConnectedSources>(_sources);
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgery());
    }

    [Fact]
    public void Shows_the_photos_and_videos_found_in_a_connected_drive()
    {
        _sources.Sources.Add(Summary(1, photos: 12, videos: 3));

        var page = Render<ConnectedAccounts>();

        Assert.Equal("ana@example.com", page.Find(".connection-account strong").TextContent);
        Assert.Equal("Photos: 12", page.Find(".counts .photos").TextContent);
        Assert.Equal("Videos: 3", page.Find(".counts .videos").TextContent);
        Assert.Equal("Connected", page.Find(".status").TextContent);
        Assert.Equal(["Count photos and videos", "Disconnect"], page.FindAll(".connection-actions button").Select(b => b.TextContent));
    }

    [Theory]
    [InlineData(Cultures.English, new[] { "Count photos and videos for ana@example.com", "Disconnect ana@example.com", "Count photos and videos for bruno@example.com", "Disconnect bruno@example.com" })]
    [InlineData(Cultures.BrazilianPortuguese, new[] { "Contar fotos e vídeos de ana@example.com", "Desconectar ana@example.com", "Contar fotos e vídeos de bruno@example.com", "Desconectar bruno@example.com" })]
    public void Gives_each_account_s_buttons_names_that_tell_them_apart_and_start_with_the_visible_text(string culture, string[] names)
    {
        using var _ = Cultures.Use(culture);
        _sources.Sources.Add(Summary(1));
        _sources.Sources.Add(Summary(2) with { AccountEmail = "bruno@example.com" });

        var buttons = Render<ConnectedAccounts>().FindAll(".connection-actions button");

        Assert.Equal(names, buttons.Select(b => b.GetAttribute("aria-label")));
        Assert.All(buttons, b => Assert.StartsWith(b.TextContent, b.GetAttribute("aria-label"), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Cultures.English, "Reconnect ana@example.com")]
    [InlineData(Cultures.BrazilianPortuguese, "Reconectar ana@example.com")]
    public void Names_the_account_on_its_reconnect_link(string culture, string name)
    {
        using var _ = Cultures.Use(culture);
        _sources.Sources.Add(Summary(1) with { Status = ConnectedSourceStatuses.Reconnect });

        Assert.Equal(name, Render<ConnectedAccounts>().Find(".connection-actions a.button").GetAttribute("aria-label"));
    }

    [Fact]
    public void Says_an_account_is_not_counted_yet_and_offers_to_count_it()
    {
        _sources.Sources.Add(Summary(1));

        var page = Render<ConnectedAccounts>();

        Assert.Contains("Not counted yet", page.Find(".connection").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".counts"));
        Assert.Equal("1", page.Find("form input[name=SourceId]").GetAttribute("value"));
    }

    [Theory]
    [InlineData(Cultures.English, "Connection expired", "Reconnect")]
    [InlineData(Cultures.BrazilianPortuguese, "Conexão expirada", "Reconectar")]
    public void Offers_to_reconnect_an_account_whose_token_expired_instead_of_counting_it(string culture, string status, string reconnect)
    {
        using var _ = Cultures.Use(culture);
        _sources.Sources.Add(Summary(1, status: ConnectedSourceStatuses.Reconnect));

        var page = Render<ConnectedAccounts>();

        Assert.Equal(status, page.Find(".status").TextContent);
        var link = page.Find(".connection-actions a.button");
        Assert.Equal(reconnect, link.TextContent);
        Assert.Equal("sources/google-drive/connect", link.GetAttribute("href"));
        Assert.Single(page.FindAll(".connection-actions button"));
    }

    [Fact]
    public void Offers_to_connect_google_drive_when_there_are_no_accounts_yet()
    {
        var page = Render<ConnectedAccounts>();

        Assert.Equal("Connect Google Drive", page.Find("a.connect").TextContent);
        Assert.Equal("sources/google-drive/connect", page.Find("a.connect").GetAttribute("href"));
        Assert.Equal("No account is connected yet.", page.Find("p.empty").TextContent);
    }

    [Theory]
    [InlineData("connected", "Google Drive connected.")]
    [InlineData("denied", "Google Drive was not connected: access was not granted.")]
    [InlineData("reconnect", "The access to this account is no longer accepted. Reconnect it.")]
    [InlineData("disconnected", "Disconnected. Nothing changed in the cloud account; you can connect it again at any time.")]
    public void Shows_what_happened_from_a_note_in_the_address_and_nothing_else(string note, string text)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/sources?note={note}");

        Assert.Equal(text, Render<ConnectedAccounts>().Find("p.note").TextContent);
    }

    [Fact]
    public void Ignores_a_note_it_does_not_know()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/sources?note=%3Cb%3Ehello%3C/b%3E");

        Assert.Empty(Render<ConnectedAccounts>().FindAll("p.note[role=status]"));
    }

    [Fact]
    public void Writes_the_page_in_brazilian_portuguese()
    {
        using var _ = Cultures.Use(Cultures.BrazilianPortuguese);
        _sources.Sources.Add(Summary(1, photos: 1200, videos: 3));

        var page = Render<ConnectedAccounts>();

        Assert.Equal("Origens", page.Find("h1").TextContent);
        Assert.Equal("Fotos: 1.200", page.Find(".counts .photos").TextContent);
        Assert.Equal("Vídeos: 3", page.Find(".counts .videos").TextContent);
        Assert.Equal(["Contar fotos e vídeos", "Desconectar"], page.FindAll(".connection-actions button").Select(b => b.TextContent));
        Assert.Equal("Conectar o Google Drive", page.Find("a.connect").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "Sources")]
    [InlineData(Cultures.BrazilianPortuguese, "Origens")]
    public void The_layout_links_to_the_sources(string culture, string text)
    {
        using var _ = Cultures.Use(culture);
        AddAuthorization().SetAuthorized("ana@example.com");

        var layout = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

        Assert.Contains(layout.FindAll("header a.nav-link"), a => a.GetAttribute("href") == "sources" && a.TextContent == text);
    }

    private static ConnectedSourceSummary Summary(long id, int? photos = null, int? videos = null, string status = ConnectedSourceStatuses.Connected) =>
        new(id, ConnectedSourceTypes.GoogleDrive, "ana@example.com", status, DateTimeOffset.UnixEpoch, null, photos, videos, photos is null ? null : DateTimeOffset.UnixEpoch);

    private sealed class FixedAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}

/// <summary>The addresses of connecting a Google Drive, on the real app with fakes (S-037): no test reaches Google.</summary>
public sealed class SourceEndpointTests
{
    [Fact]
    public async Task Sends_the_browser_to_google_with_a_state_and_a_cookie_for_it()
    {
        await using var web = Web(new FakeConnectedSources());
        using var http = web.CreateSignedInClient(followRedirects: false);

        var response = await http.GetAsync(new Uri(SourceEndpoints.ConnectPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("accounts.example.com", location.Host);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("http://localhost/sources/google-drive/callback", query["redirect_uri"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(SourceEndpoints.StateCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/sources/google-drive", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Goes_back_to_the_page_with_a_note_when_google_sign_in_is_not_set_up()
    {
        await using var web = Web(new FakeConnectedSources { CanConnectGoogleDrive = false });
        using var http = web.CreateSignedInClient(followRedirects: false);

        var response = await http.GetAsync(new Uri(SourceEndpoints.ConnectPath, UriKind.Relative));

        Assert.Equal("/sources?note=not-configured", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Completes_the_connection_with_the_code_when_the_state_comes_back_to_the_same_browser_and_person()
    {
        var sources = new FakeConnectedSources();
        await using var web = Web(sources);
        using var http = web.CreateSignedInClient(followRedirects: false);
        var state = await StartAsync(http);

        var response = await http.GetAsync(new Uri($"{SourceEndpoints.CallbackPath}?code=the-code&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        Assert.Equal("/sources?note=connected", response.Headers.Location!.OriginalString);
        Assert.Equal([("the-code", "http://localhost/sources/google-drive/callback")], sources.Completed);
    }

    [Fact]
    public async Task Says_failed_when_the_connection_cannot_be_completed()
    {
        await using var web = Web(new FakeConnectedSources { Completes = SourceOutcome.Failed });
        using var http = web.CreateSignedInClient(followRedirects: false);
        var state = await StartAsync(http);

        var response = await http.GetAsync(new Uri($"{SourceEndpoints.CallbackPath}?code=c&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        Assert.Equal("/sources?note=failed", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("access_denied", "denied")]
    [InlineData("server_error", "failed")]
    public async Task Reports_what_google_said_when_it_sends_an_error_instead_of_a_code(string error, string note)
    {
        var sources = new FakeConnectedSources();
        await using var web = Web(sources);
        using var http = web.CreateSignedInClient(followRedirects: false);
        var state = await StartAsync(http);

        var response = await http.GetAsync(new Uri($"{SourceEndpoints.CallbackPath}?error={error}&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        Assert.Equal($"/sources?note={note}", response.Headers.Location!.OriginalString);
        Assert.Empty(sources.Completed);
    }

    [Fact]
    public async Task Refuses_a_callback_with_no_state_a_forged_one_or_no_cookie()
    {
        var sources = new FakeConnectedSources();
        await using var web = Web(sources);
        using var started = web.CreateSignedInClient(followRedirects: false);
        var state = await StartAsync(started);
        using var withoutCookie = web.CreateSignedInClient(followRedirects: false);

        foreach (var (client, query) in new[]
        {
            (started, "code=c"),
            (started, "code=c&state=forged"),
            (withoutCookie, $"code=c&state={Uri.EscapeDataString(state)}"),
        })
        {
            var response = await client.GetAsync(new Uri($"{SourceEndpoints.CallbackPath}?{query}", UriKind.Relative));
            Assert.Equal("/sources?note=state", response.Headers.Location!.OriginalString);
        }

        Assert.Empty(sources.Completed);
    }

    [Fact]
    public async Task Refuses_a_state_made_for_another_person_even_with_the_right_cookie()
    {
        var sources = new FakeConnectedSources();
        await using var web = new WebFactory(new FakeLibrary()) { ConnectedSources = sources, AccessEmails = $"{TestUsers.Allowed},{TestUsers.Other}" };
        using var ana = web.CreateSignedInClient(TestUsers.Allowed, followRedirects: false);
        using var started = await ana.GetAsync(new Uri(SourceEndpoints.ConnectPath, UriKind.Relative));
        var nonce = started.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(SourceEndpoints.StateCookie + "=", StringComparison.Ordinal))
            .Split(';')[0][(SourceEndpoints.StateCookie.Length + 1)..];
        var state = HttpUtility.ParseQueryString(started.Headers.Location!.Query)["state"]!;
        using var bruno = web.CreateClientWithCookie($"{SourceEndpoints.StateCookie}={nonce}");
        bruno.DefaultRequestHeaders.Add(TestSignIn.Header, TestUsers.Other);

        var response = await bruno.GetAsync(new Uri($"{SourceEndpoints.CallbackPath}?code=c&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        Assert.Equal("/sources?note=state", response.Headers.Location!.OriginalString);
        Assert.Empty(sources.Completed);
    }

    [Fact]
    public async Task Needs_a_signed_in_person_on_the_list()
    {
        await using var web = Web(new FakeConnectedSources());
        using var anonymous = web.CreateAnonymousClient();

        var connect = await anonymous.GetAsync(new Uri(SourceEndpoints.ConnectPath, UriKind.Relative));
        var callback = await anonymous.GetAsync(new Uri(SourceEndpoints.CallbackPath + "?code=c&state=s", UriKind.Relative));
        var page = await anonymous.GetAsync(new Uri("/sources", UriKind.Relative));

        Assert.All([connect, callback, page], r =>
        {
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal("/sign-in", r.Headers.Location!.AbsolutePath);
        });
    }

    [Fact]
    public async Task The_page_lists_the_accounts_for_a_signed_in_person()
    {
        var sources = new FakeConnectedSources();
        sources.Sources.Add(new ConnectedSourceSummary(
            4, ConnectedSourceTypes.GoogleDrive, "bruno@example.com", ConnectedSourceStatuses.Connected, DateTimeOffset.UnixEpoch, null, 12, 3, DateTimeOffset.UnixEpoch));
        await using var web = Web(sources);
        using var http = web.CreateSignedInClient();

        var html = WebUtility.HtmlDecode(await http.GetStringAsync(new Uri("/sources", UriKind.Relative)));

        Assert.Contains("bruno@example.com", html, StringComparison.Ordinal);
        Assert.Contains("Photos: 12", html, StringComparison.Ordinal);
        Assert.Contains("href=\"sources\" class=\"nav-link\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Knows_a_state_only_for_the_same_nonce_and_person()
    {
        var keys = new EphemeralDataProtectionProvider();
        var state = SourceEndpoints.StateProtector(keys).Protect("nonce-1|ana@example.com", TimeSpan.FromMinutes(10));

        Assert.True(SourceEndpoints.StateIsOurs(keys, state, "nonce-1", "ANA@example.com"));
        Assert.False(SourceEndpoints.StateIsOurs(keys, state, "nonce-2", "ana@example.com"));
        Assert.False(SourceEndpoints.StateIsOurs(keys, state, "nonce-1", "bruno@example.com"));
        Assert.False(SourceEndpoints.StateIsOurs(keys, state, "nonce-1", null));
        Assert.False(SourceEndpoints.StateIsOurs(keys, state, null, "ana@example.com"));
        Assert.False(SourceEndpoints.StateIsOurs(keys, null, "nonce-1", "ana@example.com"));
        // Another key ring, as after a replaced key, or a state that has expired.
        Assert.False(SourceEndpoints.StateIsOurs(new EphemeralDataProtectionProvider(), state, "nonce-1", "ana@example.com"));
        var expired = SourceEndpoints.StateProtector(keys).Protect("nonce-1|ana@example.com", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        Assert.False(SourceEndpoints.StateIsOurs(keys, expired, "nonce-1", "ana@example.com"));
        var past = SourceEndpoints.StateProtector(keys).Protect("nonce-1|ana@example.com", DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.False(SourceEndpoints.StateIsOurs(keys, past, "nonce-1", "ana@example.com"));
    }

    [Fact]
    public async Task Makes_a_state_and_a_cookie_that_last_ten_minutes()
    {
        await using var web = Web(new FakeConnectedSources());
        using var http = web.CreateSignedInClient(followRedirects: false);
        var before = DateTimeOffset.UtcNow;

        var response = await http.GetAsync(new Uri(SourceEndpoints.ConnectPath, UriKind.Relative));

        var state = HttpUtility.ParseQueryString(response.Headers.Location!.Query)["state"]!;
        SourceEndpoints.StateProtector(web.Services.GetRequiredService<IDataProtectionProvider>()).Unprotect(state, out var expires);
        Assert.InRange(expires, before + SourceEndpoints.StateLifetime - TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow + SourceEndpoints.StateLifetime);
        Assert.Equal(TimeSpan.FromMinutes(10), SourceEndpoints.StateLifetime);
        Assert.Contains("max-age=600", response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(SourceEndpoints.StateCookie, StringComparison.Ordinal)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_state_that_has_expired_even_for_the_right_browser_and_person()
    {
        var sources = new FakeConnectedSources();
        await using var web = Web(sources);
        using var http = web.CreateSignedInClient(followRedirects: false);
        var started = await http.GetAsync(new Uri(SourceEndpoints.ConnectPath, UriKind.Relative));
        var nonce = started.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(SourceEndpoints.StateCookie + "=", StringComparison.Ordinal))
            .Split(';')[0][(SourceEndpoints.StateCookie.Length + 1)..];
        var expired = SourceEndpoints.StateProtector(web.Services.GetRequiredService<IDataProtectionProvider>())
            .Protect($"{nonce}|{TestUsers.Allowed}", DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await http.GetAsync(new Uri($"{SourceEndpoints.CallbackPath}?code=c&state={Uri.EscapeDataString(expired)}", UriKind.Relative));

        Assert.Equal("/sources?note=state", response.Headers.Location!.OriginalString);
        Assert.Empty(sources.Completed);
    }

    [Fact]
    public async Task Deletes_the_state_cookie_on_the_callback_whatever_it_comes_to()
    {
        await using var web = Web(new FakeConnectedSources());
        using var http = web.CreateSignedInClient(followRedirects: false);
        var state = await StartAsync(http);

        foreach (var query in new[] { $"code=c&state={Uri.EscapeDataString(state)}", "code=c&state=forged" })
        {
            var response = await http.GetAsync(new Uri($"{SourceEndpoints.CallbackPath}?{query}", UriKind.Relative));

            var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(SourceEndpoints.StateCookie + "=", StringComparison.Ordinal));
            Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/sources/google-drive", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static WebFactory Web(FakeConnectedSources sources) => new(new FakeLibrary()) { ConnectedSources = sources };

    // The state in the redirect to Google; the cookie that goes with it stays in the client.
    private static async Task<string> StartAsync(HttpClient http)
    {
        var response = await http.GetAsync(new Uri(SourceEndpoints.ConnectPath, UriKind.Relative));
        return HttpUtility.ParseQueryString(response.Headers.Location!.Query)["state"]!;
    }
}

/// <summary>The connected accounts on a real PostgreSQL catalog, with a Drive that never reaches Google (S-037).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ConnectedSourcesCatalogTests(CatalogFixture catalog)
{
    private const string Code = "4/the-code";
    private const string Redirect = "https://app.example.com/sources/google-drive/callback";

    [Fact]
    public async Task Stores_the_token_encrypted_so_that_the_database_never_holds_it_in_clear_text()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        var sources = Sources(db, drive, out _);

        var outcome = await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);

        Assert.Equal(SourceOutcome.Done, outcome);
        var stored = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.Equal((ConnectedSourceTypes.GoogleDrive, "ana@example.com", ConnectedSourceStatuses.Connected), (stored.Type, stored.AccountEmail, stored.Status));
        Assert.NotEqual(drive.RefreshToken, stored.EncryptedRefreshToken);
        Assert.DoesNotContain("refresh-token", stored.EncryptedRefreshToken, StringComparison.Ordinal);
        // Not in any column of the row either.
        var row = await db.Database.SqlQuery<string>($"SELECT row_to_json(c)::text AS \"Value\" FROM connected_sources c").SingleAsync();
        Assert.DoesNotContain("refresh-token", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Counts_the_photos_and_videos_of_a_fake_drive_with_12_images_and_3_videos()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive { Media = new DriveMediaCount(12, 3) };
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);
        var id = (await sources.ListAsync()).Single().Id;

        var outcome = await sources.CountAsync(id);

        Assert.Equal(SourceOutcome.Done, outcome);
        var listed = Assert.Single(await sources.ListAsync());
        Assert.Equal((12, 3), (listed.PhotoCount, listed.VideoCount));
        Assert.NotNull(listed.CountedAt);
        // The count used the plain token, read back from the protected value.
        Assert.Equal(drive.RefreshToken, drive.TokensUsed.Last());
    }

    [Fact]
    public async Task An_invalid_grant_makes_the_status_reconnect_and_keeps_the_last_counts()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);
        var id = (await sources.ListAsync()).Single().Id;
        await sources.CountAsync(id);
        drive.Failure = () => new CloudAuthorizationException("invalid_grant");

        var outcome = await sources.CountAsync(id);

        Assert.Equal(SourceOutcome.Reconnect, outcome);
        var listed = Assert.Single(await sources.ListAsync());
        Assert.True(listed.NeedsReconnecting);
        Assert.Equal((12, 3), (listed.PhotoCount, listed.VideoCount));
    }

    [Fact]
    public async Task Connecting_the_same_account_again_replaces_its_token_and_clears_the_reconnect_status_keeping_the_counts()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive { Email = "Ana@Example.com" };
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);
        var id = (await sources.ListAsync()).Single().Id;
        await sources.CountAsync(id);
        var first = (await db.ConnectedSources.AsNoTracking().SingleAsync()).EncryptedRefreshToken;
        drive.Failure = () => new CloudAuthorizationException("invalid_grant");
        await sources.CountAsync(id);
        drive.Failure = null;
        drive.RefreshToken = "1//a-new-refresh-token";

        await sources.CompleteGoogleDriveConnectionAsync("4/other", Redirect);

        var listed = Assert.Single(await sources.ListAsync());
        Assert.Equal((id, ConnectedSourceStatuses.Connected, 12), (listed.Id, listed.Status, listed.PhotoCount));
        Assert.NotEqual(first, (await db.ConnectedSources.AsNoTracking().SingleAsync()).EncryptedRefreshToken);
        // Counting uses the new token.
        await sources.CountAsync(id);
        Assert.Equal("1//a-new-refresh-token", drive.TokensUsed.Last());
    }

    [Fact]
    public async Task Two_accounts_are_two_connections()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);
        drive.Email = "bruno@example.com";
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);

        Assert.Equal(["ana@example.com", "bruno@example.com"], (await sources.ListAsync()).Select(s => s.AccountEmail));
    }

    [Fact]
    public async Task A_refused_code_or_an_unreachable_google_connects_nothing()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive { RefreshToken = null };
        var sources = Sources(db, drive, out _);

        Assert.Equal(SourceOutcome.Failed, await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect));
        drive.RefreshToken = "1//token";
        drive.Failure = () => new HttpRequestException("Google is down");
        Assert.Equal(SourceOutcome.Failed, await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect));

        Assert.Empty(await sources.ListAsync());
    }

    [Fact]
    public async Task A_failure_that_is_not_an_invalid_grant_leaves_the_status_and_the_counts_alone()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);
        var id = (await sources.ListAsync()).Single().Id;
        drive.Failure = () => new HttpRequestException("Google is down");

        Assert.Equal(SourceOutcome.Failed, await sources.CountAsync(id));

        var listed = Assert.Single(await sources.ListAsync());
        Assert.Equal((ConnectedSourceStatuses.Connected, (int?)null), (listed.Status, listed.PhotoCount));
    }

    [Fact]
    public async Task A_token_the_key_ring_can_no_longer_read_needs_reconnecting_without_asking_google()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        await Sources(db, drive, out _).CompleteGoogleDriveConnectionAsync(Code, Redirect);
        var id = await db.ConnectedSources.Select(s => s.Id).SingleAsync();
        drive.TokensUsed.Clear();
        // A new key ring, as after the keys were lost.
        var replaced = Sources(db, drive, out _, new EphemeralDataProtectionProvider());

        Assert.Equal(SourceOutcome.Reconnect, await replaced.CountAsync(id));

        Assert.Empty(drive.TokensUsed);
        Assert.True((await replaced.ListAsync()).Single().NeedsReconnecting);
    }

    [Fact]
    public async Task Trims_and_lower_cases_the_address_google_gives()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var sources = Sources(db, new FakeDrive { Email = "  Ana@Example.COM \n" }, out _);

        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);

        Assert.Equal("ana@example.com", (await db.ConnectedSources.AsNoTracking().SingleAsync()).AccountEmail);
    }

    [Fact]
    public async Task A_callback_that_loses_the_race_for_a_new_account_updates_the_row_the_other_one_made()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var connection = catalog.ConnectionStringFor(db);
        var drive = new FakeDrive { RefreshToken = "1//the-later-token" };
        var rival = new RivalInsert(connection);
        var sources = new ConnectedSources(
            new TestContexts(connection, rival), new DriveTokenProtector(s_keys), drive, drive, new FakeOneDrive(), new FakeOneDrive(), TimeProvider.System, NullLogger<ConnectedSources>.Instance);

        var outcome = await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);

        Assert.Equal(SourceOutcome.Done, outcome);
        Assert.True(rival.Fired);
        var row = await db.ConnectedSources.AsNoTracking().SingleAsync();
        Assert.Equal(ConnectedSourceStatuses.Connected, row.Status);
        Assert.Equal("1//the-later-token", new DriveTokenProtector(s_keys).Unprotect(row.EncryptedRefreshToken));
    }

    [Fact]
    public async Task A_timeout_that_is_not_the_caller_giving_up_is_a_failure_not_an_error()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);
        var id = (await sources.ListAsync()).Single().Id;
        drive.Failure = () => new TaskCanceledException("The request timed out.");

        Assert.Equal(SourceOutcome.Failed, await sources.CountAsync(id));
        drive.RevokeFailure = new TaskCanceledException("The request timed out.");
        Assert.Equal(SourceOutcome.Done, await sources.DisconnectAsync(id));
        drive.Failure = () => new TaskCanceledException("The request timed out.");
        Assert.Equal(SourceOutcome.Failed, await Sources(db, drive, out _).CompleteGoogleDriveConnectionAsync(Code, Redirect));
    }

    [Fact]
    public async Task Gives_up_when_the_caller_does()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);
        var id = (await sources.ListAsync()).Single().Id;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        drive.Failure = () => new OperationCanceledException(cancelled.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sources.CountAsync(id, cancelled.Token));
    }

    [Fact]
    public async Task Disconnecting_deletes_the_token_and_asks_google_to_forget_it()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive();
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);
        var id = (await sources.ListAsync()).Single().Id;

        var outcome = await sources.DisconnectAsync(id);

        Assert.Equal(SourceOutcome.Done, outcome);
        Assert.Equal(0, await db.ConnectedSources.CountAsync());
        Assert.Equal([drive.RefreshToken!], drive.Revoked);
        Assert.Equal(SourceOutcome.NotFound, await sources.DisconnectAsync(id));
        Assert.Equal(SourceOutcome.NotFound, await sources.CountAsync(id));
    }

    [Fact]
    public async Task Disconnecting_deletes_the_token_even_when_google_cannot_be_told()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var drive = new FakeDrive { RevokeFailure = new HttpRequestException("Google is down") };
        var sources = Sources(db, drive, out _);
        await sources.CompleteGoogleDriveConnectionAsync(Code, Redirect);

        Assert.Equal(SourceOutcome.Done, await sources.DisconnectAsync((await sources.ListAsync()).Single().Id));

        Assert.Equal(0, await db.ConnectedSources.CountAsync());
    }

    [Fact]
    public async Task The_catalog_refuses_a_status_that_is_not_one_and_a_second_row_for_an_account()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.ConnectedSources.Add(Row("ana@example.com", "connected"));
        await db.SaveChangesAsync();

        db.ConnectedSources.Add(Row("ana@example.com", "connected"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.ConnectedSources.Add(Row("bruno@example.com", "broken"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // Inserts the same account from another context just before the first save, as a second callback would have.
    private sealed class RivalInsert(string connectionString) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var other = new TestContexts(connectionString).CreateDbContext();
                other.ConnectedSources.Add(Row("ana@example.com", ConnectedSourceStatuses.Reconnect));
                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    private static ConnectedSource Row(string email, string status) => new()
    {
        Type = ConnectedSourceTypes.GoogleDrive,
        AccountEmail = email,
        EncryptedRefreshToken = "protected",
        Status = status,
    };

    private ConnectedSources Sources(CatalogDbContext db, FakeDrive drive, out DriveTokenProtector tokens, IDataProtectionProvider? keys = null)
    {
        keys ??= s_keys;
        tokens = new DriveTokenProtector(keys);
        return new ConnectedSources(
            new TestContexts(catalog.ConnectionStringFor(db)), tokens, drive, drive, new FakeOneDrive(), new FakeOneDrive(), TimeProvider.System, NullLogger<ConnectedSources>.Instance);
    }

    // One key ring for the tests that connect and then count, as the app's.
    private static readonly IDataProtectionProvider s_keys = new EphemeralDataProtectionProvider();
}

/// <summary>Google sign-in not set up (S-037): the page says so and offers no connection.</summary>
public sealed class SourcesPageWithoutGoogleTests : LocalizedContext
{
    public SourcesPageWithoutGoogleTests()
    {
        Services.AddSingleton<IConnectedSources>(new FakeConnectedSources { CanConnectGoogleDrive = false });
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgery());
    }

    [Fact]
    public void Says_google_drive_cannot_be_connected_yet_and_shows_no_button()
    {
        var page = Render<ConnectedAccounts>();

        Assert.Empty(page.FindAll("a.connect"));
        Assert.Contains("Google sign-in is not set up", page.Find("p.note").TextContent, StringComparison.Ordinal);
    }

    private sealed class FixedAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
