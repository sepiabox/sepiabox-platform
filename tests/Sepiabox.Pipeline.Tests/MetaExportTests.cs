using System.Text.Json;
using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

/// <summary>The JSON helpers the Meta exports share (S-048, D-111), over hand-written JSON.</summary>
public sealed class MetaExportTests
{
    [Theory]
    [InlineData("aniversÃ¡rio", "aniversário")]
    [InlineData("SÃ£o Paulo", "São Paulo")]
    [InlineData("ð\u009f\u0098\u008a", "\U0001F60A")]
    [InlineData("aniversário", "aniversário")]
    [InlineData("plain ascii", "plain ascii")]
    [InlineData("", "")]
    [InlineData("café", "café")]
    [InlineData("Ã", "Ã")]
    [InlineData("aniversÃ¡rio €", "aniversÃ¡rio €")]
    [InlineData("日本語", "日本語")]
    public void Repairs_escaped_utf8_only_when_the_round_trip_is_valid(string text, string expected) =>
        Assert.Equal(expected, MetaExport.RepairText(text));

    [Theory]
    [InlineData("1561975200", "2019-07-01T10:00:00")]
    [InlineData("\"1561975200\"", "2019-07-01T10:00:00")]
    [InlineData("\"+1561975200\"", "2019-07-01T10:00:00")]
    [InlineData("-86400", "1969-12-31T00:00:00")]
    public void Reads_epoch_seconds_as_utc(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        var date = MetaExport.Epoch(document.RootElement);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date);
        Assert.Equal(DateTimeKind.Utc, date!.Value.Kind);
    }

    [Theory]
    [InlineData("\"soon\"")]
    [InlineData("99999999999999999")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("1.5")]
    public void Reads_no_date_from_anything_else(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Null(MetaExport.Epoch(document.RootElement));
    }

    [Theory]
    [InlineData("2019:07:14 16:30:05", "2019-07-14T16:30:05", DateTimeKind.Unspecified)]
    [InlineData("2019-07-14T16:30:05", "2019-07-14T16:30:05", DateTimeKind.Unspecified)]
    [InlineData("2019-07-14 16:30:05+01:00", "2019-07-14T16:30:05", DateTimeKind.Unspecified)]
    [InlineData("2019-07-14T16:30:05Z", "2019-07-14T16:30:05", DateTimeKind.Utc)]
    public void Reads_camera_dates_as_written(string text, string expected, DateTimeKind kind)
    {
        var date = MetaExport.ExifTime(text);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date);
        Assert.Equal(kind, date!.Value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("July 14")]
    [InlineData("2019:13:45 16:30:05")]
    public void Reads_no_camera_date_from_anything_else(string text) => Assert.Null(MetaExport.ExifTime(text));

    [Fact]
    public void Reads_the_place_and_the_date_from_photo_or_video_exif_data()
    {
        using var photo = JsonDocument.Parse("""{"photo_metadata": {"exif_data": [{"iso": 100}, {"latitude": 38.7223, "longitude": -9.1393, "date_time_original": "2019:07:14 16:30:05"}]}}""");
        using var video = JsonDocument.Parse("""{"video_metadata": {"exif_data": [{"latitude": "41.1579", "longitude": "-8.6291"}]}}""");

        var fromPhoto = MetaExport.Exif(photo.RootElement)!;
        var fromVideo = MetaExport.Exif(video.RootElement)!;

        Assert.Equal(new GeoPoint(38.7223, -9.1393), fromPhoto.Location);
        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 5), fromPhoto.TakenAt);
        Assert.Equal(new GeoPoint(41.1579, -8.6291), fromVideo.Location);
        Assert.Null(fromVideo.TakenAt);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"photo_metadata": {"exif_data": []}}""")]
    [InlineData("""{"photo_metadata": {"exif_data": [{"latitude": 38.7}]}}""")]
    [InlineData("""{"photo_metadata": "none"}""")]
    [InlineData("""[]""")]
    public void Reads_nothing_from_exif_data_without_a_place_pair_or_a_date(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Null(MetaExport.Exif(document.RootElement));
    }

    [Fact]
    public void Finds_the_media_of_a_list_of_posts_with_the_post_title_and_time_or_their_own()
    {
        using var document = JsonDocument.Parse("""
            [
              {"media": [{"uri": "media/posts/a.jpg", "creation_timestamp": 1561975200, "title": "Praia"}]},
              {"title": "Trip", "creation_timestamp": 1561975300, "media": [
                {"uri": "media/posts/b.jpg"},
                {"uri": "media/posts/c.jpg", "creation_timestamp": 1561975400},
                {"uri": "media/posts/d.jpg", "title": "Own"}]}
            ]
            """);

        var items = MetaExport.Items(document.RootElement).ToList();

        Assert.Equal(["media/posts/a.jpg", "media/posts/b.jpg", "media/posts/c.jpg", "media/posts/d.jpg"], items.Select(i => i.Uri));
        Assert.Equal(["Praia", "Trip", "Trip", "Own"], items.Select(i => i.Title));
        Assert.Equal(
            [1561975200L, 1561975300L, 1561975400L, 1561975300L],
            items.Select(i => new DateTimeOffset(i.PostedAt!.Value, TimeSpan.Zero).ToUnixTimeSeconds()));
    }

    [Fact]
    public void Finds_media_in_objects_with_a_wrapping_key_and_repairs_their_titles()
    {
        using var document = JsonDocument.Parse("""{"ig_stories": [{"uri": "media/stories/s.mp4", "creation_timestamp": 1561975200, "title": "aniversÃ¡rio"}]}""");

        var item = Assert.Single(MetaExport.Items(document.RootElement));

        Assert.Equal("aniversário", item.Title);
        Assert.Equal("media/stories/s.mp4", item.Uri);
    }

    [Fact]
    public void Ignores_values_that_are_not_what_they_should_be()
    {
        using var document = JsonDocument.Parse("""[{"uri": 5}, {"uri": ""}, {"title": 3, "creation_timestamp": "x", "media": [{"uri": "a.jpg", "media_metadata": 7}]}, "text", null]""");

        var item = Assert.Single(MetaExport.Items(document.RootElement));

        Assert.Equal("a.jpg", item.Uri);
        Assert.Null(item.Title);
        Assert.Null(item.PostedAt);
        Assert.Null(item.Exif);
    }

    [Fact]
    public void Stops_at_the_deepest_nesting_and_says_so_instead_of_silently()
    {
        var json = string.Concat(Enumerable.Repeat("[", 200)) + """{"uri": "a.jpg"}""" + string.Concat(Enumerable.Repeat("]", 200));
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 300 });
        var reported = 0;

        Assert.Empty(MetaExport.Items(document.RootElement, () => reported++));

        Assert.True(reported > 0);
    }

    [Fact]
    public void Reads_facebooks_taken_timestamp_as_utc_and_prefers_a_camera_date_string()
    {
        using var facebook = JsonDocument.Parse("""{"photo_metadata": {"exif_data": [{"taken_timestamp": 1561975200, "latitude": 38.7, "longitude": -9.1}]}}""");
        using var both = JsonDocument.Parse("""{"photo_metadata": {"exif_data": [{"date_time_original": "2019:06:30 18:00:00", "taken_timestamp": 1561975200}]}}""");

        var taken = MetaExport.Exif(facebook.RootElement)!.TakenAt!.Value;

        Assert.Equal(new DateTime(2019, 7, 1, 10, 0, 0, DateTimeKind.Utc), taken);
        Assert.Equal(new DateTime(2019, 6, 30, 18, 0, 0), MetaExport.Exif(both.RootElement)!.TakenAt);
    }

    [Fact]
    public void Takes_the_description_else_the_post_text_for_media_and_repairs_it()
    {
        using var document = JsonDocument.Parse("""
            [{"uri": "a.jpg", "description": "Ol\u00c3\u00a1"}, {"uri": "b.jpg"}, {"uri": "c.jpg", "description": "  "}]
            """);

        var items = MetaExport.Items(document.RootElement, "post text", new DateTime(2019, 7, 1, 0, 0, 0, DateTimeKind.Utc)).ToList();

        Assert.Equal(["Olá", "post text", "post text"], items.Select(i => i.Text));
        Assert.All(items, i => Assert.Equal(2019, i.PostedAt!.Value.Year));
    }
}
