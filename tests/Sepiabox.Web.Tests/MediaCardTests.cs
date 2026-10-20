using Sepiabox.Web.Components.Media;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class MediaCardTests : LocalizedContext
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
        Assert.Equal("Video, duration 0:03, August 9, 2020, 18:05", card.Find("a.thumb").GetAttribute("aria-label"));
    }

    [Fact]
    public void Says_it_is_a_video_when_the_length_is_unknown()
    {
        var card = RenderCard(new TimelineItem(Ids.Of(2), AssetKinds.Video, null, true, DurationSeconds: null));

        Assert.Single(card.FindAll(".play"));
        Assert.Equal("Video", card.Find(".badge").TextContent);
        Assert.Equal("Video, No date", card.Find("a.thumb").GetAttribute("aria-label"));
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
        Assert.Equal("Video without a thumbnail", card.Find(".placeholder").TextContent);
        Assert.Empty(card.FindAll(".play"));
        Assert.Equal("1:15", card.Find(".badge").TextContent);
    }

    [Fact]
    public void Describes_a_video_in_brazilian_portuguese_when_the_culture_is_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);

        var card = RenderCard(new TimelineItem(Ids.Of(5), AssetKinds.Video, new DateTime(2020, 8, 9, 18, 5, 0), true, 3));

        Assert.Equal("Vídeo, duração 0:03, 9 de agosto de 2020, 18:05", card.Find("a.thumb").GetAttribute("aria-label"));
    }

    [Fact]
    public void Says_what_has_no_thumbnail_in_brazilian_portuguese_when_the_culture_is_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);

        var card = RenderCard(new TimelineItem(Ids.Of(6), AssetKinds.Video, null, false, DurationSeconds: null));

        Assert.Equal("Vídeo sem miniatura", card.Find(".placeholder").TextContent);
        Assert.Equal("Vídeo", card.Find(".badge").TextContent);
        Assert.Equal("Vídeo, Sem data", card.Find("a.thumb").GetAttribute("aria-label"));
    }

    private IRenderedComponent<MediaCard> RenderCard(TimelineItem item) =>
        Render<MediaCard>(p => p.Add(c => c.Item, item).Add(c => c.Href, $"fotos/{item.Id}"));
}
