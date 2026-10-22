using System.Text;
using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

/// <summary>
/// The Facebook reader (S-049, D-112) over synthetic downloads generated here: names and JSON, no real person's data. The
/// folder layout and the field names are believed, not confirmed (D-112).
/// </summary>
public sealed class FacebookReaderTests : IDisposable
{
    private const string Album = "your_facebook_activity/posts/album/0.json";
    private const string Posts = "your_facebook_activity/posts/your_posts__check_ins__photos_and_videos_1.json";
    private readonly TempFolder _export = new();

    public void Dispose() => _export.Dispose();

    [Fact]
    public void Dates_a_photo_from_its_taken_timestamp_and_names_its_album_and_facebook()
    {
        Put("your_facebook_activity/posts/media/a.jpg");
        Json(Album, """
            {"name": "Viagem a Paris", "photos": [{"uri": "your_facebook_activity/posts/media/a.jpg", "creation_timestamp": 1561975800,
              "media_metadata": {"photo_metadata": {"exif_data": [{"taken_timestamp": 1561975200, "latitude": 48.8566, "longitude": 2.3522}]}}}]}
            """);

        var metadata = Read().Find("your_facebook_activity/posts/media/a.jpg")!;

        Assert.Equal(new DateTime(2019, 7, 1, 10, 0, 0, DateTimeKind.Utc), metadata.CapturedAt);
        Assert.Equal(["Facebook", "Viagem a Paris"], metadata.Albums.Order(StringComparer.Ordinal));
        Assert.Equal(new GeoPoint(48.8566, 2.3522), metadata.Location);
        Assert.Null(metadata.SourceUrl);
    }

    [Fact]
    public void Falls_back_to_the_creation_timestamp_and_takes_the_description_as_the_caption()
    {
        Put("media/a.jpg");
        Json(Album, """{"name": "Timeline Photos", "description": "the album", "photos": [{"uri": "media/a.jpg", "creation_timestamp": 1561975800, "title": "ignored", "description": "OlÃ¡ Paris"}]}""");

        var metadata = Read().Find("media/a.jpg")!;

        Assert.Equal(new DateTime(2019, 7, 1, 10, 10, 0, DateTimeKind.Utc), metadata.CapturedAt);
        Assert.Equal("Olá Paris", metadata.Caption);
        Assert.Equal(["Facebook", "Timeline Photos"], metadata.Albums.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Uses_the_posts_text_as_the_caption_when_the_media_has_no_description_and_merges_with_its_album()
    {
        Put("media/a.jpg");
        Put("media/b.jpg");
        Json(Album, """{"name": "Mobile uploads", "photos": [{"uri": "media/a.jpg", "creation_timestamp": 1561975800}]}""");
        Json(Posts, """
            [{"timestamp": 1561976000, "data": [{"post": "Dia na praia"}], "attachments": [{"data": [
              {"media": {"uri": "media/a.jpg", "creation_timestamp": 1561975800}},
              {"media": {"uri": "media/b.jpg", "description": "own words"}}]}]}]
            """);

        var scan = Read();

        var a = scan.Find("media/a.jpg")!;
        Assert.Equal("Dia na praia", a.Caption);
        Assert.Equal(["Facebook", "Mobile uploads"], a.Albums.Order(StringComparer.Ordinal));
        var b = scan.Find("media/b.jpg")!;
        Assert.Equal("own words", b.Caption);
        // The post's time when the media has none.
        Assert.Equal(new DateTime(2019, 7, 1, 10, 13, 20, DateTimeKind.Utc), b.CapturedAt);
        Assert.Equal(["Facebook"], b.Albums);
    }

    [Fact]
    public void Reads_both_layouts_with_and_without_the_activity_prefix_the_same()
    {
        Put("photos/a.jpg");
        Put("old/photos/a.jpg");
        var json = """{"name": "Paris", "photos": [{"uri": "photos/a.jpg", "creation_timestamp": 1561975200, "description": "x"}]}""";
        Json("your_facebook_activity/posts/album/0.json", json);
        Json("old/posts/album/0.json", json);

        var scan = Read();

        var current = scan.Find("photos/a.jpg")!;
        var older = scan.Find("old/photos/a.jpg")!;
        Assert.Equal((current.CapturedAt, current.Caption, current.Albums.Count), (older.CapturedAt, older.Caption, older.Albums.Count));
        Assert.Equal("x", older.Caption);
    }

    [Fact]
    public void Counts_a_uri_with_a_nul_as_missing_and_reads_the_good_entry_next_to_it()
    {
        Put("media/a.jpg");
        Json(Album, "{\"name\": \"Paris\", \"photos\": [{\"uri\": \"media/b\\u0000.jpg\"}, {\"uri\": \"media/a.jpg\"}]}");

        var scan = Read();

        Assert.Equal(1, scan.Count);
        Assert.Contains(scan.Warnings, w => w.Contains("1 media files named in the lists are not in the download", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaves_out_the_media_no_list_names_and_counts_them()
    {
        Put("media/a.jpg");
        Put("messages/inbox/chat/photos/m.jpg");
        Json(Album, """{"name": "Paris", "photos": [{"uri": "media/a.jpg"}]}""");

        var scan = Read();

        Assert.True(scan.IsLeftOut("messages/inbox/chat/photos/m.jpg"));
        Assert.Equal(1, scan.Count);
        var warning = Assert.Single(scan.Warnings);
        Assert.Contains("1 photos and videos in the download are in no album or post list", warning, StringComparison.Ordinal);
        Assert.Contains("Check this number against your account", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_missing_files_and_unreadable_lists_and_keeps_going()
    {
        Put("media/a.jpg");
        Json(Album, """{"name": "Paris", "photos": [{"uri": "media/a.jpg"}, {"uri": "media/gone.jpg"}]}""");
        Json("your_facebook_activity/posts/album/1.json", "{ not json");

        var scan = Read();

        Assert.Equal(1, scan.Count);
        Assert.Contains(scan.Warnings, w => w.Contains("1 media files named in the lists are not in the download", StringComparison.Ordinal));
        Assert.Contains(scan.Warnings, w => w.Contains("1 lists could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void Lists_an_album_named_facebook_once()
    {
        Put("media/a.jpg");
        Json(Album, """{"name": "Facebook", "photos": [{"uri": "media/a.jpg"}]}""");

        Assert.Equal(["Facebook"], Read().Find("media/a.jpg")!.Albums);
    }

    [Fact]
    public void Gives_an_album_without_a_name_only_the_facebook_album_and_ignores_a_cover_photo_and_odd_shapes()
    {
        Put("media/a.jpg");
        Put("media/cover.jpg");
        Json(Album, """{"cover_photo": {"uri": "media/cover.jpg"}, "photos": [{"uri": "media/a.jpg"}]}""");
        Json("your_facebook_activity/posts/album/1.json", """["not an album"]""");

        var scan = Read();

        Assert.Equal(["Facebook"], scan.Find("media/a.jpg")!.Albums);
        Assert.Null(scan.Find("media/cover.jpg"));
    }

    [Fact]
    public void Stops_with_a_message_when_the_download_has_no_list_or_does_not_exist_and_changes_nothing()
    {
        Put("media/a.jpg");

        var none = Assert.Throws<ExportReadException>(() => Read());
        var missing = Assert.Throws<ExportReadException>(() => new FacebookReader().Read(Path.Combine(_export.Path, "missing")));

        Assert.Contains("posts/album/0.json", none.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", missing.Message, StringComparison.Ordinal);
        Json(Album, """{"name": "x", "photos": [{"uri": "media/a.jpg"}]}""");
        var before = Snapshot();
        Read();
        Assert.Equal(before, Snapshot());
        Assert.True(ExportReaderRegistry.Default.TryGet("facebook", out var reader));
        Assert.IsType<FacebookReader>(reader);
    }

    private ExportScan Read() => new FacebookReader().Read(_export.Path);

    private void Put(string relative) => _export.Write(relative, [1, 2, 3]);

    private void Json(string relative, string json) => _export.Write(relative, Encoding.UTF8.GetBytes(json));

    private List<(string, long, DateTime)> Snapshot() =>
        [.. Directory.GetFiles(_export.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f, new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];
}
