using Sepiabox.Web.Components.Media;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class VideoPlayerTests : BunitContext
{
    private const string Label = "Vídeo de 9 de agosto de 2020, 18:05";

    [Fact]
    public void Plays_the_720p_version_through_the_signing_endpoint_with_its_poster_when_it_exists()
    {
        var player = RenderPlayer(Ids.Of(1), DerivativeNames.Poster, hasProxy: true);

        var video = player.Find(".player video");
        Assert.Equal($"fotos/{Ids.Of(1)}/proxy-720p", video.GetAttribute("src"));
        Assert.Equal($"fotos/{Ids.Of(1)}/poster", video.GetAttribute("poster"));
        Assert.Equal(Label, video.GetAttribute("aria-label"));
        Assert.True(video.HasAttribute("controls"));
        // Only the length and first frame on opening the page, not the whole file.
        Assert.Equal("metadata", video.GetAttribute("preload"));
        Assert.Empty(player.FindAll(".processing"));
        Assert.DoesNotContain("sig=", player.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Has_the_hooks_video_js_needs_and_a_hidden_error_message_when_it_plays()
    {
        var player = RenderPlayer(Ids.Of(2), DerivativeNames.Poster, hasProxy: true);

        // video.js listens for errors on `video[data-player]` and reveals `.player-error` in the same `.player`.
        var video = player.Find(".player video[data-player]");
        Assert.Empty(video.Children);
        var message = player.Find(".player .player-error");
        // Visible but empty (CSS hides it while empty): video.js only sets its text, which screen readers announce.
        Assert.False(message.HasAttribute("hidden"));
        Assert.Equal("alert", message.GetAttribute("role"));
        Assert.Equal(string.Empty, message.TextContent);
        Assert.Equal("Não foi possível reproduzir o vídeo. Descarregue o original.", message.GetAttribute("data-message"));
    }

    [Fact]
    public void Plays_without_a_poster_when_the_video_has_none()
    {
        var player = RenderPlayer(Ids.Of(3), posterName: null, hasProxy: true);

        Assert.False(player.Find("video").HasAttribute("poster"));
    }

    [Fact]
    public void Shows_the_poster_and_says_it_is_still_processing_without_the_720p_version()
    {
        var player = RenderPlayer(Ids.Of(4), DerivativeNames.Poster, hasProxy: false);

        Assert.Empty(player.FindAll("video"));
        Assert.Empty(player.FindAll(".player-error"));
        var poster = player.Find("img");
        Assert.Equal($"fotos/{Ids.Of(4)}/poster", poster.GetAttribute("src"));
        Assert.Equal(Label, poster.GetAttribute("alt"));
        var processing = player.Find(".processing");
        Assert.Equal("Vídeo ainda em processamento", processing.TextContent);
        // Static text, present when the page loads: nothing to announce as a change.
        Assert.False(processing.HasAttribute("role"));
    }

    [Fact]
    public void Only_says_it_is_still_processing_without_any_derivative()
    {
        var player = RenderPlayer(Ids.Of(5), posterName: null, hasProxy: false);

        Assert.Empty(player.FindAll("video"));
        Assert.Empty(player.FindAll("img"));
        Assert.Equal("Vídeo ainda em processamento", player.Find(".processing").TextContent);
    }

    private IRenderedComponent<VideoPlayer> RenderPlayer(string id, string? posterName, bool hasProxy) =>
        Render<VideoPlayer>(p => p
            .Add(v => v.AssetId, id)
            .Add(v => v.Label, Label)
            .Add(v => v.PosterName, posterName)
            .Add(v => v.HasProxy, hasProxy));
}
