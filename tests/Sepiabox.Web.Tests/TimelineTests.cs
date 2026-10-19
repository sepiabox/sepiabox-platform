using System.Web;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class TimelineTests : BunitContext
{
    [Fact]
    public void Assets_from_three_months_show_three_month_groups_newest_first()
    {
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary(
            new TimelineItem(Ids.Of(1), "photo", new DateTime(2026, 8, 3, 10, 0, 0), $"{Ids.Of(1)}/thumb-256.webp"),
            new TimelineItem(Ids.Of(2), "photo", new DateTime(2026, 10, 1, 9, 0, 0), $"{Ids.Of(2)}/thumb-256.webp"),
            new TimelineItem(Ids.Of(3), "video", new DateTime(2025, 12, 24, 20, 0, 0), $"{Ids.Of(3)}/thumb-256.webp")));
        Services.AddSingleton<ISasUrlFactory>(new FakeLinks());

        var page = Render<Timeline>();

        var titles = page.FindAll("section.month h2").Select(h => h.TextContent).ToList();
        Assert.Equal(["outubro de 2026", "agosto de 2026", "dezembro de 2025"], titles);
        Assert.Equal(3, page.FindAll("ul.grid img").Count);
    }

    [Fact]
    public void An_asset_without_a_date_appears_last_under_sem_data()
    {
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary(
            new TimelineItem(Ids.Of(4), "photo", null, $"{Ids.Of(4)}/thumb-256.webp"),
            new TimelineItem(Ids.Of(5), "photo", new DateTime(2019, 7, 14, 10, 32, 0), $"{Ids.Of(5)}/thumb-256.webp")));
        Services.AddSingleton<ISasUrlFactory>(new FakeLinks());

        var page = Render<Timeline>();

        var groups = page.FindAll("section.month");
        Assert.Equal(2, groups.Count);
        Assert.Equal("Sem data", groups[^1].QuerySelector("h2")!.TextContent);
        Assert.Contains($"fotos/{Ids.Of(4)}", groups[^1].InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Thumbnails_are_read_only_links_that_expire_within_15_minutes()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary(
            new TimelineItem(Ids.Of(6), "photo", new DateTime(2026, 10, 2), $"{Ids.Of(6)}/thumb-256.webp")));
        Services.AddSingleton<ISasUrlFactory>(new SasUrlFactory(new BlobServiceClient("UseDevelopmentStorage=true"), new FixedTime(now)));

        var page = Render<Timeline>();

        var src = new Uri(page.Find("ul.grid img").GetAttribute("src")!);
        var query = HttpUtility.ParseQueryString(src.Query);
        Assert.Equal("r", query["sp"]);
        var expiry = DateTimeOffset.Parse(query["se"]!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(expiry <= now.AddMinutes(15), $"Expiry {expiry:O} is more than 15 minutes after {now:O}.");
    }

    [Fact]
    public void A_full_page_links_to_the_older_page()
    {
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary(
            new TimelineItem(Ids.Of(7), "photo", new DateTime(2026, 1, 1), null))
        { HasMore = true });
        Services.AddSingleton<ISasUrlFactory>(new FakeLinks());

        var page = Render<Timeline>();

        Assert.Equal("?pagina=2", page.Find("nav.pages a").GetAttribute("href"));
        Assert.Contains("sem miniatura", page.Find("ul.grid .placeholder").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_library_says_how_to_add_photos()
    {
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary());
        Services.AddSingleton<ISasUrlFactory>(new FakeLinks());

        var page = Render<Timeline>();

        Assert.Contains("sepiabox-ingest", page.Find("p.empty").TextContent, StringComparison.Ordinal);
    }
}
