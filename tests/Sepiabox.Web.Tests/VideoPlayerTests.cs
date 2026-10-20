using Sepiabox.Web.Components.Media;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class VideoPlayerTests : BunitContext
{
    [Fact]
    public void Plays_the_720p_version_through_the_signing_endpoint_with_its_poster()
    {
        var player = Render<VideoPlayer>(p => p
            .Add(v => v.AssetId, Ids.Of(1))
            .Add(v => v.PosterName, DerivativeNames.Poster)
            .Add(v => v.HasProxy, true));

        var video = player.Find("video");
        Assert.Equal($"fotos/{Ids.Of(1)}/proxy-720p", video.GetAttribute("src"));
        Assert.Equal($"fotos/{Ids.Of(1)}/poster", video.GetAttribute("poster"));
        Assert.True(video.HasAttribute("controls"));
        // Only the length and first frame on opening the page, not the whole file.
        Assert.Equal("metadata", video.GetAttribute("preload"));
        Assert.Empty(player.FindAll(".processing"));
        Assert.DoesNotContain("sig=", player.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_video_without_a_poster_plays_without_one()
    {
        var player = Render<VideoPlayer>(p => p
            .Add(v => v.AssetId, Ids.Of(2))
            .Add(v => v.HasProxy, true));

        Assert.False(player.Find("video").HasAttribute("poster"));
    }

    [Fact]
    public void Without_the_720p_version_shows_the_poster_and_says_it_is_still_processing()
    {
        var player = Render<VideoPlayer>(p => p
            .Add(v => v.AssetId, Ids.Of(3))
            .Add(v => v.PosterName, DerivativeNames.Poster)
            .Add(v => v.HasProxy, false));

        Assert.Empty(player.FindAll("video"));
        Assert.Equal($"fotos/{Ids.Of(3)}/poster", player.Find("img").GetAttribute("src"));
        Assert.Equal("Vídeo ainda em processamento", player.Find(".processing").TextContent);
    }

    [Fact]
    public void Without_any_derivative_only_says_it_is_still_processing()
    {
        var player = Render<VideoPlayer>(p => p.Add(v => v.AssetId, Ids.Of(4)));

        Assert.Empty(player.FindAll("video"));
        Assert.Empty(player.FindAll("img"));
        Assert.Equal("Vídeo ainda em processamento", player.Find(".processing").TextContent);
    }
}
