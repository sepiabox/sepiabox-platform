using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;
using Sepiabox.Web.Services;
using static Sepiabox.Web.Tests.SignInWeb;

namespace Sepiabox.Web.Tests;

/// <summary>The language of whole pages, end to end: Accept-Language, the culture cookie and the switch (D-050).</summary>
public sealed partial class LocalizationWebTests
{
    private static readonly DateTime PhotoDate = new(2019, 7, 14, 10, 32, 0);

    [Fact]
    public async Task Renders_the_timeline_in_english_when_the_browser_asks_for_english()
    {
        await using var web = CreateWithPhoto();
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");

        var html = await GetPageAsync(http, "/");

        Assert.Equal("en", HtmlLang(html));
        Assert.Contains("<h2>July 2019</h2>", html, StringComparison.Ordinal);
        Assert.Contains("Photos and videos", html, StringComparison.Ordinal);
        Assert.Contains(">Sign out<", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pt-PT")]
    [InlineData("pt-BR")]
    [InlineData("pt")]
    [InlineData("de-DE,pt;q=0.8")]
    public async Task Renders_the_timeline_in_brazilian_portuguese_when_the_browser_asks_for_any_portuguese(string acceptLanguage)
    {
        await using var web = CreateWithPhoto();
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);

        var html = await GetPageAsync(http, "/");

        Assert.Equal("pt-BR", HtmlLang(html));
        Assert.Contains("<h2>julho de 2019</h2>", html, StringComparison.Ordinal);
        Assert.Contains("Fotos e vídeos", html, StringComparison.Ordinal);
        Assert.Contains(">Sair<", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData(null)]
    public async Task Renders_a_page_in_english_when_the_browser_asks_for_no_supported_language(string? acceptLanguage)
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();
        if (acceptLanguage is not null)
        {
            http.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
        }

        var html = await GetPageAsync(http, "/entrar");

        Assert.Equal("en", HtmlLang(html));
        Assert.Contains("<h1>Sign in</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renders_a_page_in_english_when_the_cookie_says_english_and_the_browser_portuguese()
    {
        await using var web = CreateWithPhoto();
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR");
        http.DefaultRequestHeaders.Add("Cookie", CultureCookie("en"));

        var html = await GetPageAsync(http, $"/fotos/{Photo}");

        Assert.Equal("en", HtmlLang(html));
        Assert.Contains("Download the original", html, StringComparison.Ordinal);
        Assert.Contains("July 14, 2019, 10:32", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_to_the_same_photo_in_brazilian_portuguese_when_the_switch_chooses_it()
    {
        await using var web = CreateWithPhoto();
        using var http = web.CreateSignedInClient(followRedirects: false);
        http.DefaultRequestHeaders.Add("Accept-Language", "en-US");
        var detail = $"/fotos/{Photo}?pagina=2";
        var before = await GetPageAsync(http, detail);
        Assert.Contains("Download the original", before, StringComparison.Ordinal);

        using var response = await SwitchAsync(http, detail, "pt-BR", detail);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(detail, response.Headers.Location!.OriginalString);
        var cookie = Assert.Single(SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList()),
            c => c.Name == CookieRequestCultureProvider.DefaultCookieName);
        Assert.Equal("c=pt-BR|uic=pt-BR", Uri.UnescapeDataString(cookie.Value.ToString()));
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
        Assert.NotNull(cookie.MaxAge);

        var after = await GetPageAsync(http, response.Headers.Location.OriginalString);
        Assert.Equal("pt-BR", HtmlLang(after));
        Assert.Contains("Baixar o original", after, StringComparison.Ordinal);
        Assert.Contains("14 de julho de 2019, 10:32", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switches_back_to_english_over_a_portuguese_browser()
    {
        await using var web = CreateWithPhoto();
        using var http = web.CreateSignedInClient(followRedirects: false);
        http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR");

        using var response = await SwitchAsync(http, "/", "en", "/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await GetPageAsync(http, "/");
        Assert.Equal("en", HtmlLang(html));
        Assert.Contains("<h2>July 2019</h2>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switches_the_sign_in_page_and_stays_on_it_without_signing_in()
    {
        await using var web = Create(settings: BothClients);
        using var http = web.CreateAnonymousClient();
        var signIn = "/entrar?ReturnUrl=%2Ffotos%2Fabc";

        using var response = await SwitchAsync(http, signIn, "pt-BR", signIn);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(signIn, response.Headers.Location!.OriginalString);
        var html = await GetPageAsync(http, signIn);
        Assert.Equal("pt-BR", HtmlLang(html));
        Assert.Contains("Entrar com o Google", html, StringComparison.Ordinal);
        Assert.Contains("href=\"entrar/google?returnUrl=%2Ffotos%2Fabc\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switches_the_no_access_page_and_stays_on_it()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await SwitchAsync(http, "/sem-acesso", "pt-BR", "/sem-acesso");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sem-acesso", response.Headers.Location!.OriginalString);
        var html = await GetPageAsync(http, "/sem-acesso");
        Assert.Contains("<h1>Sem acesso</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Sair e entrar com outra conta", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("//evil.example.com/")]
    [InlineData("/\\evil.example.com/")]
    [InlineData("")]
    public async Task Returns_home_when_the_switch_is_given_a_return_url_outside_the_app(string returnUrl)
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await SwitchAsync(http, "/entrar", "pt-BR", returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("pt-PT")]
    [InlineData("de")]
    [InlineData("")]
    public async Task Refuses_a_culture_the_app_does_not_have(string culture)
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await SwitchAsync(http, "/entrar", culture, "/entrar");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie") && response.Headers.GetValues("Set-Cookie")
            .Any(c => c.StartsWith(CookieRequestCultureProvider.DefaultCookieName + "=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Refuses_a_switch_without_the_antiforgery_token()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await http.PostAsync(Url("/language"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "pt-BR",
            ["returnUrl"] = "/entrar",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Bare: status code pages do not turn it into a page.
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Returns_home_when_the_return_url_is_not_escaped()
    {
        await using var web = Create();
        using var http = web.CreateAnonymousClient();

        using var response = await SwitchAsync(http, "/entrar", "pt-BR", "/fotos/é");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Marks_the_culture_cookie_secure_over_https()
    {
        await using var web = Create();
        using var http = web.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        using var response = await SwitchAsync(http, "/entrar", "pt-BR", "/entrar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookie = Assert.Single(SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList()),
            c => c.Name == CookieRequestCultureProvider.DefaultCookieName);
        Assert.True(cookie.Secure);
    }

    [Fact]
    public async Task Offers_to_switch_back_to_the_page_that_was_not_found_not_to_the_not_found_page()
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient();

        using var response = await http.GetAsync(Url("/no-such-page?x=1"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("/no-such-page?x=1", SwitchReturnUrl(html));
    }

    [Theory]
    [InlineData("en-US", "Something went wrong.")]
    [InlineData("pt-BR", "Algo deu errado.")]
    public async Task Offers_to_switch_back_to_the_page_that_failed_not_to_the_error_page(string acceptLanguage, string heading)
    {
        var library = new FakeLibrary { TimelineFailure = new InvalidOperationException("The catalog is down.") };
        await using var web = new WebFactory(library) { Environment = "Production" };
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);

        using var response = await http.GetAsync(Url("/?pagina=2"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains($"<h1>{heading}</h1>", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Equal("/?pagina=2", SwitchReturnUrl(html));
    }

    [Theory]
    [InlineData("en-US", "Page not found")]
    [InlineData("pt-PT", "Página não encontrada")]
    public async Task Renders_the_not_found_page_in_the_browser_language(string acceptLanguage, string expected)
    {
        await using var web = Create();
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);

        using var response = await http.GetAsync(Url("/no-such-page"));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains($"<h1>{expected}</h1>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "Couldn't play the video. Download the original.", "Video still processing")]
    [InlineData("pt-BR", "Não foi possível reproduzir o vídeo. Baixe o original.", "Vídeo ainda em processamento")]
    public async Task Gives_the_player_and_the_processing_message_in_the_browser_language(string acceptLanguage, string playbackError, string processing)
    {
        var library = new FakeLibrary();
        library.Assets[Ids.Of(2)] = Details.Video(Ids.Of(2));
        library.Assets[Ids.Of(3)] = Details.Video(Ids.Of(3), hasProxy: false);
        await using var web = new WebFactory(library);
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);

        var playing = await GetPageAsync(http, $"/fotos/{Ids.Of(2)}");
        var waiting = await GetPageAsync(http, $"/fotos/{Ids.Of(3)}");

        Assert.Contains($"data-message=\"{playbackError}\"", playing, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"processing\">{processing}</p>", waiting, StringComparison.Ordinal);
    }

    private static WebFactory CreateWithPhoto()
    {
        var library = new FakeLibrary(new TimelineItem(Photo, AssetKinds.Photo, PhotoDate, true, DurationSeconds: null));
        library.Assets[Photo] = Details.Photo(Photo, PhotoDate);
        return new WebFactory(library);
    }

    // Decoded, so a test reads the text as the browser shows it ("Couldn't", not "Couldn&#x27;t").
    private static async Task<string> GetPageAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(Url(path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Uses the language switch on <paramref name="page"/> as a browser would: its form, its token.</summary>
    private static async Task<HttpResponseMessage> SwitchAsync(HttpClient http, string page, string culture, string returnUrl)
    {
        var token = await FormTokenAsync(http, page);
        return await http.PostAsync(Url("/language"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["culture"] = culture,
            ["returnUrl"] = returnUrl,
        }));
    }

    private static string? SwitchReturnUrl(string html) =>
        SwitchReturnUrlInput().Match(html) is { Success: true } match ? WebUtility.HtmlDecode(match.Groups["url"].Value) : null;

    [GeneratedRegex("<form class=\"language\"[^>]*>.*?name=\"returnUrl\" value=\"(?<url>[^\"]*)\"", RegexOptions.Singleline)]
    private static partial Regex SwitchReturnUrlInput();

    private static string CultureCookie(string culture) =>
        $"{CookieRequestCultureProvider.DefaultCookieName}={Uri.EscapeDataString(CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)))}";

    private static string? HtmlLang(string html) => HtmlLangAttribute().Match(html) is { Success: true } match ? match.Groups["lang"].Value : null;

    [GeneratedRegex("<html lang=\"(?<lang>[^\"]*)\"")]
    private static partial Regex HtmlLangAttribute();
}
