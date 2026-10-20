using Sepiabox.Web.Components.Media;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class MediaCardTests : BunitContext
{
    [Fact]
    public void A_3_second_video_shows_its_poster_a_play_icon_and_0_03()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(1), AssetKinds.Video, new DateTime(2020, 8, 9, 18, 5, 0), true, 3));

        // A video's thumbnail is made from its poster.
        Assert.Equal($"fotos/{Ids.Of(1)}/thumb-256", card.Find("img").GetAttribute("src"));
        Assert.Single(card.FindAll(".play svg"));
        Assert.Equal("true", card.Find(".play").GetAttribute("aria-hidden"));
        Assert.Equal("0:03", card.Find(".badge").TextContent);
        Assert.Equal("Vídeo, 0:03, 9 de agosto de 2020, 18:05", card.Find("a.thumb").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_video_of_unknown_length_still_says_it_is_a_video()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(2), AssetKinds.Video, null, true));

        Assert.Single(card.FindAll(".play"));
        Assert.Equal("Vídeo", card.Find(".badge").TextContent);
        Assert.Equal("Vídeo, Sem data", card.Find("a.thumb").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_photo_has_no_play_icon_or_length()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(3), AssetKinds.Photo, new DateTime(2019, 7, 14, 10, 32, 0), true, 3));

        Assert.Empty(card.FindAll(".play"));
        Assert.Empty(card.FindAll(".badge"));
        Assert.Equal($"fotos/{Ids.Of(3)}", card.Find("a.thumb").GetAttribute("href"));
    }

    [Fact]
    public void A_video_without_a_thumbnail_says_so_and_keeps_its_length()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(4), AssetKinds.Video, null, false, 75));

        Assert.Empty(card.FindAll("img"));
        Assert.Equal("Vídeo sem miniatura", card.Find(".placeholder").TextContent);
        Assert.Equal("1:15", card.Find(".badge").TextContent);
    }

    private IRenderedComponent<MediaCard> RenderCard(TimelineItem item) =>
        Render<MediaCard>(p => p.Add(c => c.Item, item).Add(c => c.Href, $"fotos/{item.Id}"));
}
