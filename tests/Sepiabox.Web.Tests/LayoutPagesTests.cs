using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components;
using Sepiabox.Web.Components.Layout;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The error, not-found and reconnect text, the layout's error bar and the ended-session block, in both cultures.</summary>
public sealed class LayoutPagesTests : LocalizedContext
{
    public LayoutPagesTests()
    {
        AddAuthorization();
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgery());
    }

    [Theory]
    [InlineData(Cultures.English, "Something went wrong.", "The request couldn't be completed. Please try again shortly.", "Request ID:")]
    [InlineData(Cultures.BrazilianPortuguese, "Algo deu errado.", "Não foi possível concluir a solicitação. Tente novamente daqui a pouco.", "Código da solicitação:")]
    public void The_error_page_is_in_the_culture(string culture, string heading, string text, string requestId)
    {
        using var scope = Cultures.Use(culture);
        var http = new DefaultHttpContext { TraceIdentifier = "trace-1" };

        var page = Render<Error>(p => p.AddCascadingValue<HttpContext>(http));

        Assert.Equal(heading, page.Find("h1").TextContent);
        Assert.Equal(text, page.FindAll("p")[0].TextContent);
        Assert.Equal(requestId, page.Find("strong").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "Page not found", "This page doesn't exist.")]
    [InlineData(Cultures.BrazilianPortuguese, "Página não encontrada", "Esta página não existe.")]
    public void The_not_found_page_is_in_the_culture(string culture, string heading, string text)
    {
        using var scope = Cultures.Use(culture);

        var page = Render<NotFound>();

        Assert.Equal(heading, page.Find("h1").TextContent);
        Assert.Equal(text, page.Find("p").TextContent);
    }

    [Theory]
    [InlineData(Cultures.English, "Reconnecting…", "Connection lost. Trying again in  seconds.", "Try again", "Resume")]
    [InlineData(Cultures.BrazilianPortuguese, "Reconectando…", "Sem conexão. Nova tentativa em  segundos.", "Tentar novamente", "Retomar")]
    public void The_reconnect_modal_is_in_the_culture(string culture, string first, string repeated, string retry, string resume)
    {
        using var scope = Cultures.Use(culture);

        var modal = Render<ReconnectModal>();

        Assert.Equal(first, Text(modal, ".components-reconnect-first-attempt-visible"));
        // The countdown span stays inside the sentence for Blazor's script to fill.
        Assert.Equal(repeated, Text(modal, ".components-reconnect-repeated-attempt-visible"));
        Assert.NotNull(modal.Find(".components-reconnect-repeated-attempt-visible #components-seconds-to-next-attempt"));
        Assert.Equal(retry, Text(modal, "#components-reconnect-button"));
        Assert.Equal(resume, Text(modal, "#components-resume-button"));
    }

    [Theory]
    [InlineData(Cultures.English, "An unexpected error has occurred.", "Reload", "Close")]
    [InlineData(Cultures.BrazilianPortuguese, "Ocorreu um erro inesperado.", "Recarregar", "Fechar")]
    public void The_layout_s_error_bar_is_in_the_culture(string culture, string text, string reload, string close)
    {
        using var scope = Cultures.Use(culture);

        var layout = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

        var bar = layout.Find("#blazor-error-ui");
        Assert.StartsWith(text, bar.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal(reload, bar.QuerySelector("a.reload")!.TextContent);
        Assert.Equal(close, bar.QuerySelector("button.dismiss")!.GetAttribute("aria-label"));
        Assert.NotNull(layout.Find("header form.language"));
    }

    [Theory]
    [InlineData(Cultures.English, "No access", "This session no longer has access to Sepiabox.", "Sign in")]
    [InlineData(Cultures.BrazilianPortuguese, "Sem acesso", "Esta sessão não tem mais acesso ao Sepiabox.", "Entrar")]
    public void A_session_that_lost_access_is_told_so_in_the_culture(string culture, string heading, string text, string link)
    {
        using var scope = Cultures.Use(culture);
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/");

        var routes = Render<Routes>();

        var block = routes.Find("section.auth");
        Assert.Equal(heading, block.QuerySelector("h1")!.TextContent);
        Assert.Equal(text, block.QuerySelector("p")!.TextContent);
        Assert.Equal(link, block.QuerySelector("a.button")!.TextContent);
    }

    private static string Text(IRenderedComponent<ReconnectModal> modal, string selector) =>
        modal.Find(selector).TextContent.Trim();

    private sealed class FixedAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
