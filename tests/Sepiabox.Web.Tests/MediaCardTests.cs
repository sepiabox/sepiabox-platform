using Sepiabox.Web.Components.Media;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class MediaCardTests : BunitContext
{
    [Fact]
    public void Shows_a_play_icon_and_0_03_when_the_video_lasts_3_seconds()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(1), AssetKinds.Video, new DateTime(2020, 8, 9, 18, 5, 0), true, 3));

        // A video's thumbnail is made from its poster.
        Assert.Equal($"fotos/{Ids.Of(1)}/thumb-256", card.Find("img").GetAttribute("src"));
        Assert.Single(card.FindAll(".play svg"));
        Assert.Equal("true", card.Find(".play").GetAttribute("aria-hidden"));
        Assert.Equal("0:03", card.Find(".badge").TextContent);
        Assert.Equal("Vídeo, duração 0:03, 9 de agosto de 2020, 18:05", card.Find("a.thumb").GetAttribute("aria-label"));
    }

    [Fact]
    public void Says_it_is_a_video_when_the_length_is_unknown()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(2), AssetKinds.Video, null, true, DurationSeconds: null));

        Assert.Single(card.FindAll(".play"));
        Assert.Equal("Vídeo", card.Find(".badge").TextContent);
        Assert.Equal("Vídeo, Sem data", card.Find("a.thumb").GetAttribute("aria-label"));
    }

    [Fact]
    public void Shows_no_play_icon_or_length_when_the_asset_is_a_photo()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(3), AssetKinds.Photo, new DateTime(2019, 7, 14, 10, 32, 0), true, 3));

        Assert.Empty(card.FindAll(".play"));
        Assert.Empty(card.FindAll(".badge"));
        Assert.Equal($"fotos/{Ids.Of(3)}", card.Find("a.thumb").GetAttribute("href"));
    }

    [Fact]
    public void Keeps_the_length_without_a_play_icon_when_the_video_has_no_thumbnail()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(4), AssetKinds.Video, null, false, 75));

        Assert.Empty(card.FindAll("img"));
        Assert.Equal("Vídeo sem miniatura", card.Find(".placeholder").TextContent);
        Assert.Empty(card.FindAll(".play"));
        Assert.Equal("1:15", card.Find(".badge").TextContent);
    }

    private IRenderedComponent<MediaCard> RenderCard(TimelineItem item) =>
        Render<MediaCard>(p => p.Add(c => c.Item, item).Add(c => c.Href, $"fotos/{item.Id}"));
}
