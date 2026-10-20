using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Sepiabox.Web.Auth;
using Sepiabox.Web.Components.Layout;
using Sepiabox.Web.Components.Pages;

namespace Sepiabox.Web.Tests;

/// <summary>The sign-in and no-access pages and the language switch, in English and Brazilian Portuguese.</summary>
public sealed class AuthPagesTests : LocalizedContext
{
    private static readonly SignInProviders Both = new(new OAuthClient("google-id", "not-a-secret"), new OAuthClient("microsoft-id", "not-a-secret"));

    public AuthPagesTests()
    {
        AddAuthorization();
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgery());
    }

    [Theory]
    [InlineData(Cultures.English, "Sign in", "Sign in with Google", "Sign in with Microsoft", "Sepiabox is private.")]
    [InlineData(Cultures.BrazilianPortuguese, "Entrar", "Entrar com o Google", "Entrar com a Microsoft", "O Sepiabox é privado.")]
    public void The_sign_in_page_has_a_button_per_provider_in_the_culture(string culture, string heading, string google, string microsoft, string intro)
    {
        using var scope = Cultures.Use(culture);

        var page = RenderSignIn(Both, "Production");

        Assert.Equal(heading, page.Find("h1").TextContent);
        Assert.StartsWith(intro, page.Find("section.auth p").TextContent, StringComparison.Ordinal);
        Assert.Equal([google, microsoft], page.FindAll(".providers a.button").Select(a => a.TextContent));
        Assert.Empty(page.FindAll("p.error"));
    }

    [Theory]
    [InlineData(Cultures.English, "Couldn't sign in. Please try again.")]
    [InlineData(Cultures.BrazilianPortuguese, "Não foi possível entrar. Tente novamente.")]
    public void A_failed_sign_in_says_so_in_the_culture(string culture, string expected)
    {
        using var scope = Cultures.Use(culture);

        var page = RenderSignIn(Both, "Production", query: $"?{AuthSetup.SignInErrorQuery}=1");

        Assert.Equal(expected, page.Find("p.error").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "E-mail (development only)", "Sign in")]
    [InlineData(Cultures.BrazilianPortuguese, "E-mail (só em desenvolvimento)", "Entrar")]
    public void The_development_form_is_labelled_in_the_culture(string culture, string label, string button)
    {
        using var scope = Cultures.Use(culture);

        var page = RenderSignIn(new SignInProviders(null, null), "Development");

        Assert.Equal(label, page.Find("form.dev-login label").TextContent);
        Assert.Equal(button, page.Find("form.dev-login button").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "Sign-in isn't set up yet.")]
    [InlineData(Cultures.BrazilianPortuguese, "A entrada ainda não está configurada.")]
    public void Says_sign_in_is_not_set_up_in_the_culture(string culture, string expected)
    {
        using var scope = Cultures.Use(culture);

        var page = RenderSignIn(new SignInProviders(null, null), "Production");

        Assert.Equal(expected, page.Find("p.empty").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "No access", "Sign in with another account")]
    [InlineData(Cultures.BrazilianPortuguese, "Sem acesso", "Entrar com outra conta")]
    public void The_no_access_page_offers_another_account_in_the_culture(string culture, string heading, string link)
    {
        using var scope = Cultures.Use(culture);

        var page = Render<NoAccess>();

        Assert.Equal(heading, page.Find("h1").TextContent);
        Assert.Equal(link, page.Find(".providers a.button").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "This account doesn't have access to Sepiabox.", "Sign out and sign in with another account")]
    [InlineData(Cultures.BrazilianPortuguese, "Esta conta não tem acesso ao Sepiabox.", "Sair e entrar com outra conta")]
    public void The_no_access_page_offers_to_sign_out_in_the_culture_when_signed_in(string culture, string text, string button)
    {
        using var scope = Cultures.Use(culture);
        var auth = AddAuthorization();
        auth.SetAuthorized(TestUsers.Stranger);

        var page = Render<NoAccess>();

        Assert.StartsWith(text, page.Find("section.auth p").TextContent, StringComparison.Ordinal);
        Assert.Equal(button, page.Find(".providers form.sign-out button").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "Sign out")]
    [InlineData(Cultures.BrazilianPortuguese, "Sair")]
    public void The_sign_out_button_is_labelled_in_the_culture(string culture, string expected)
    {
        using var scope = Cultures.Use(culture);

        var button = Render<SignOutButton>();

        Assert.Equal(expected, button.Find("button").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "true", "false")]
    [InlineData(Cultures.BrazilianPortuguese, "false", "true")]
    public void The_language_switch_offers_both_languages_and_returns_to_the_same_page(string culture, string englishPressed, string portuguesePressed)
    {
        using var scope = Cultures.Use(culture);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/fotos/{Ids.Of(1)}?pagina=2");

        var form = Render<LanguageSwitch>().Find("form.language");

        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Equal("language", form.GetAttribute("action"));
        Assert.Equal("false", form.GetAttribute("data-enhance"));
        Assert.Equal($"/fotos/{Ids.Of(1)}?pagina=2", form.QuerySelector("input[name=returnUrl]")!.GetAttribute("value"));
        Assert.NotNull(form.QuerySelector("input[name=__RequestVerificationToken]"));
        var buttons = form.QuerySelectorAll("button[name=culture]");
        Assert.Equal(["en", "pt-BR"], buttons.Select(b => b.GetAttribute("value")));
        // Each language is named in itself, whatever the page's language.
        Assert.Equal(["English", "Português (Brasil)"], buttons.Select(b => b.TextContent));
        Assert.Equal([englishPressed, portuguesePressed], buttons.Select(b => b.GetAttribute("aria-pressed")));
    }

    private IRenderedComponent<SignIn> RenderSignIn(SignInProviders providers, string environment, string query = "")
    {
        Services.AddSingleton(providers);
        Services.AddSingleton<IWebHostEnvironment>(new FakeEnvironment(environment));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/entrar" + query);
        var http = new DefaultHttpContext();
        return Render<SignIn>(p => p.AddCascadingValue<HttpContext>(http));
    }

    private sealed class FixedAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }

    private sealed class FakeEnvironment(string name) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Sepiabox.Web";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = name;
    }
}
