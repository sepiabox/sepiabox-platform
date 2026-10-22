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

    [Theory]
    [InlineData("\u00ff", "\u00ff")]
    [InlineData("\u0100", "\u0100")]
    [InlineData("\u007f", "\u007f")]
    [InlineData("\u0080", "\u0080")]
    [InlineData("\u00c2\u00a9", "\u00a9")]
    [InlineData("\u00e2\u0082\u00ac", "\u20ac")]
    [InlineData("\u00c3\u00a1\u0100", "\u00c3\u00a1\u0100")]
    public void Repairs_only_inside_the_latin_1_range_and_leaves_the_bounds_alone(string text, string expected) =>
        Assert.Equal(expected, MetaExport.RepairText(text));

    [Theory]
    [InlineData("253402300799", "9999-12-31T23:59:59")]
    [InlineData("-62135596800", "0001-01-01T00:00:00")]
    public void Reads_the_first_and_the_last_second_of_the_calendar(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), MetaExport.Epoch(document.RootElement));
    }

    [Theory]
    [InlineData("253402300800")]
    [InlineData("-62135596801")]
    public void Reads_no_date_one_second_past_the_calendar(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Null(MetaExport.Epoch(document.RootElement));
    }

    [Fact]
    public void Takes_the_place_and_the_date_of_the_first_entry_that_has_them()
    {
        using var document = JsonDocument.Parse("""
            {"photo_metadata": {"exif_data": [
              {"latitude": 38.7, "longitude": -9.1, "date_time_original": "2019:06:30 18:00:00"},
              {"latitude": 41.1, "longitude": -8.6, "date_time_original": "2018:01:01 10:00:00"}]}}
            """);

        var exif = MetaExport.Exif(document.RootElement)!;

        Assert.Equal(new GeoPoint(38.7, -9.1), exif.Location);
        Assert.Equal(new DateTime(2019, 6, 30, 18, 0, 0), exif.TakenAt);
    }

    [Fact]
    public void Lets_an_empty_title_inherit_the_posts_and_finds_media_nested_in_objects()
    {
        using var document = JsonDocument.Parse("""{"title": "Post", "media": [{"uri": "a.jpg", "title": ""}], "wrapper": {"inner": {"uri": "b.jpg"}}}""");

        var items = MetaExport.Items(document.RootElement).ToList();

        Assert.Equal(["a.jpg", "b.jpg"], items.Select(i => i.Uri));
        Assert.Equal(["Post", "Post"], items.Select(i => i.Title));
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Follows_the_nesting_to_depth_64_and_no_deeper(int depth, bool found)
    {
        var json = string.Concat(Enumerable.Repeat("[", depth)) + """{"uri": "a.jpg"}""" + string.Concat(Enumerable.Repeat("]", depth));
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 300 });

        Assert.Equal(found, MetaExport.Items(document.RootElement).Any());
    }
}
