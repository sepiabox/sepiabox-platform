using System.Text;
using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

/// <summary>
/// The Instagram reader (S-048, D-111) over synthetic downloads generated here: names and JSON, no real person's data. The
/// folder layout and the field names are believed, not confirmed (D-111).
/// </summary>
public sealed class InstagramReaderTests : IDisposable
{
    private const string Posts = "your_instagram_activity/content/posts_1.json";
    private readonly TempFolder _export = new();

    public void Dispose() => _export.Dispose();

    [Fact]
    public void Reads_the_posting_time_as_utc_the_caption_and_the_instagram_album()
    {
        Put("media/posts/201907/a.jpg");
        Json(Posts, """[{"media": [{"uri": "media/posts/201907/a.jpg", "creation_timestamp": 1561975200, "title": "Praia"}]}]""");

        var metadata = Read().Find("media/posts/201907/a.jpg")!;

        Assert.Equal(new DateTime(2019, 7, 1, 10, 0, 0, DateTimeKind.Utc), metadata.CapturedAt);
        Assert.Equal("Praia", metadata.Caption);
        Assert.Equal(["Instagram"], metadata.Albums);
        Assert.Null(metadata.SourceUrl);
    }

    [Fact]
    public void Gives_each_media_of_a_carousel_the_posts_caption_and_its_own_time_when_it_has_one()
    {
        Put("media/posts/a.jpg");
        Put("media/posts/b.jpg");
        Put("media/posts/c.jpg");
        Json(Posts, """
            [{"title": "Weekend", "creation_timestamp": 1561975200, "media": [
              {"uri": "media/posts/a.jpg"},
              {"uri": "media/posts/b.jpg", "creation_timestamp": 1561975800},
              {"uri": "media/posts/c.jpg", "creation_timestamp": 1561976400}]}]
            """);

        var scan = Read();

        Assert.All(["a", "b", "c"], n => Assert.Equal("Weekend", scan.Find($"media/posts/{n}.jpg")!.Caption));
        Assert.Equal(new DateTime(2019, 7, 1, 10, 0, 0, DateTimeKind.Utc), scan.Find("media/posts/a.jpg")!.CapturedAt);
        Assert.Equal(new DateTime(2019, 7, 1, 10, 10, 0, DateTimeKind.Utc), scan.Find("media/posts/b.jpg")!.CapturedAt);
        Assert.Equal(new DateTime(2019, 7, 1, 10, 20, 0, DateTimeKind.Utc), scan.Find("media/posts/c.jpg")!.CapturedAt);
    }

    [Fact]
    public void Prefers_the_cameras_date_and_place_to_the_posting_time()
    {
        Put("media/posts/a.jpg");
        Json(Posts, """
            [{"media": [{"uri": "media/posts/a.jpg", "creation_timestamp": 1561975200, "media_metadata": {"photo_metadata": {"exif_data": [
              {"latitude": 38.7223, "longitude": -9.1393, "date_time_original": "2019:06:30 18:00:00"}]}}}]}]
            """);

        var metadata = Read().Find("media/posts/a.jpg")!;

        Assert.Equal(new DateTime(2019, 6, 30, 18, 0, 0), metadata.CapturedAt);
        Assert.Equal(DateTimeKind.Unspecified, metadata.CapturedAt!.Value.Kind);
        Assert.Equal(new GeoPoint(38.7223, -9.1393), metadata.Location);
    }

    [Fact]
    public void Repairs_a_caption_written_as_escaped_utf8_and_leaves_a_right_one_alone()
    {
        Put("media/posts/a.jpg");
        Put("media/posts/b.jpg");
        Directory.CreateDirectory(Path.Combine(_export.Path, "content"));
        File.WriteAllText(
            Path.Combine(_export.Path, "content/posts_1.json"),
            "[{\"media\": [{\"uri\": \"media/posts/a.jpg\", \"title\": \"anivers\\u00c3\\u00a1rio\"}]}, {\"media\": [{\"uri\": \"media/posts/b.jpg\", \"title\": \"Pôr do sol\"}]}]",
            new UTF8Encoding(false));

        var scan = Read();

        Assert.Equal("aniversário", scan.Find("media/posts/a.jpg")!.Caption);
        Assert.Equal("Pôr do sol", scan.Find("media/posts/b.jpg")!.Caption);
    }

    [Fact]
    public void Reads_the_older_layout_without_the_activity_folder_and_a_download_inside_another_folder()
    {
        Put("media/posts/a.jpg");
        Put("Download/media/posts/b.jpg");
        Json("content/posts_1.json", """[{"media": [{"uri": "media/posts/a.jpg", "title": "old"}]}]""");
        Json("Download/your_instagram_activity/content/posts_1.json", """[{"media": [{"uri": "media/posts/b.jpg", "title": "nested"}]}]""");

        var scan = Read();

        Assert.Equal("old", scan.Find("media/posts/a.jpg")!.Caption);
        Assert.Equal("nested", scan.Find("Download/media/posts/b.jpg")!.Caption);
    }

    [Fact]
    public void Adds_the_stories_and_reels_albums_to_the_instagram_one_and_merges_a_file_in_two_lists()
    {
        Put("media/stories/s.jpg");
        Put("media/reels/r.mp4");
        Put("media/archived_posts/p.jpg");
        Json("your_instagram_activity/content/stories.json", """{"ig_stories": [{"uri": "media/stories/s.jpg", "creation_timestamp": 1561975200, "title": "story"}]}""");
        Json("your_instagram_activity/content/reels.json", """{"ig_reels_media": [{"media": [{"uri": "media/reels/r.mp4", "creation_timestamp": 1561975200}]}]}""");
        Json(Posts, """[{"media": [{"uri": "media/reels/r.mp4", "title": "also a post"}]}]""");
        Json("your_instagram_activity/content/archived_posts.json", """{"ig_archived_post_media": [{"media": [{"uri": "media/archived_posts/p.jpg", "creation_timestamp": 1561975200}]}]}""");

        var scan = Read();

        Assert.Equal(["Instagram", "Instagram stories"], scan.Find("media/stories/s.jpg")!.Albums);
        var reel = scan.Find("media/reels/r.mp4")!;
        Assert.Equal(["Instagram", "Instagram reels"], reel.Albums);
        Assert.Equal("also a post", reel.Caption);
        Assert.NotNull(reel.CapturedAt);
        Assert.Equal(["Instagram"], scan.Find("media/archived_posts/p.jpg")!.Albums);
    }

    [Fact]
    public void Leaves_out_the_media_no_list_names_and_counts_them_once()
    {
        Put("media/posts/a.jpg");
        Put("media/profile/me.jpg");
        Put("media/other/x.mp4");
        Json(Posts, """[{"media": [{"uri": "media/posts/a.jpg"}]}]""");

        var scan = Read();

        Assert.True(scan.IsLeftOut("media/profile/me.jpg"));
        Assert.True(scan.IsLeftOut("media/other/x.mp4"));
        Assert.False(scan.IsLeftOut("media/posts/a.jpg"));
        Assert.Equal(1, scan.Count);
        var warning = Assert.Single(scan.Warnings);
        Assert.Contains("2 photos and videos in the download are in no post list", warning, StringComparison.Ordinal);
        Assert.Contains("highlights, IGTV and archived stories", warning, StringComparison.Ordinal);
        Assert.Contains("Check this number against your account", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_listed_files_that_are_missing_and_paths_that_leave_the_download()
    {
        Put("media/posts/a.jpg");
        Put("outside.jpg");
        Json(Posts, """[{"media": [{"uri": "media/posts/a.jpg"}, {"uri": "media/posts/gone.jpg"}, {"uri": "../outside.jpg"}, {"uri": "notes.txt"}]}]""");

        var scan = Read();

        Assert.Equal(1, scan.Count);
        Assert.Contains(scan.Warnings, w => w.Contains("3 media files named in the post lists are not in the download", StringComparison.Ordinal));
    }

    [Fact]
    public void Reads_a_folder_named_like_dot_dot_and_still_refuses_to_leave_the_download()
    {
        Put("..hidden/a.jpg");
        Put("outside.jpg");
        Json(Posts, """[{"media": [{"uri": "..hidden/a.jpg"}, {"uri": "../outside.jpg"}, {"uri": ".."}]}]""");

        var scan = Read();

        Assert.NotNull(scan.Find("..hidden/a.jpg"));
        Assert.Equal(1, scan.Count);
    }

    [Fact]
    public void Finds_a_file_whose_name_is_on_disk_repaired_or_as_written()
    {
        Put("media/posts/café.jpg");
        Put("media/posts/cafÃ© raw.jpg");
        Json(Posts, "[{\"media\": [{\"uri\": \"media/posts/caf\\u00c3\\u00a9.jpg\"}, {\"uri\": \"media/posts/caf\\u00c3\\u00a9 raw.jpg\"}]}]");

        var scan = Read();

        Assert.NotNull(scan.Find("media/posts/café.jpg"));
        Assert.NotNull(scan.Find("media/posts/cafÃ© raw.jpg"));
    }

    [Fact]
    public void Warns_about_a_list_that_cannot_be_read_and_reads_the_others()
    {
        Put("media/posts/a.jpg");
        Json(Posts, """[{"media": [{"uri": "media/posts/a.jpg"}]}]""");
        Json("your_instagram_activity/content/stories.json", "{ not json");

        var scan = Read();

        Assert.Equal(1, scan.Count);
        Assert.Contains(scan.Warnings, w => w.Contains("1 post lists could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void Stops_with_a_message_when_the_download_has_no_post_list_or_does_not_exist()
    {
        Put("media/posts/a.jpg");

        var none = Assert.Throws<ExportReadException>(() => Read());
        var missing = Assert.Throws<ExportReadException>(() => new InstagramReader().Read(Path.Combine(_export.Path, "missing")));

        Assert.Contains("posts_1.json", none.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_nothing_in_the_download_and_is_registered_as_instagram()
    {
        Put("media/posts/a.jpg");
        Json(Posts, """[{"media": [{"uri": "media/posts/a.jpg", "title": "x"}]}]""");
        var before = Snapshot();

        Read();

        Assert.Equal(before, Snapshot());
        Assert.True(ExportReaderRegistry.Default.TryGet("instagram", out var reader));
        Assert.IsType<InstagramReader>(reader);
    }

    private ExportScan Read() => new InstagramReader().Read(_export.Path);

    private void Put(string relative) => _export.Write(relative, [1, 2, 3]);

    private void Json(string relative, string json) => _export.Write(relative, Encoding.UTF8.GetBytes(json));

    private List<(string, long, DateTime)> Snapshot() =>
        [.. Directory.GetFiles(_export.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f, new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];
}
