using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

/// <summary>
/// The Google Takeout reader (S-046, D-106) over synthetic exports generated here: only names and JSON, no photos, and
/// no real person's data.
/// </summary>
public sealed class GoogleTakeoutReaderTests : IDisposable
{
    private readonly TempFolder _export = new();

    public void Dispose() => _export.Dispose();

    [Fact]
    public void Reads_the_taken_time_as_utc_epoch_seconds()
    {
        Put("Photos from 2019/IMG_0001.jpg");
        Json("Photos from 2019/IMG_0001.jpg.json", """{"title": "IMG_0001.jpg", "photoTakenTime": {"timestamp": "1561975200", "formatted": "1 Jul 2019, 10:00:00 UTC"}}""");

        var metadata = Read().Find("Photos from 2019/IMG_0001.jpg")!;

        Assert.Equal(new DateTime(2019, 7, 1, 10, 0, 0, DateTimeKind.Utc), metadata.CapturedAt);
        Assert.Equal(DateTimeKind.Utc, metadata.CapturedAt!.Value.Kind);
    }

    [Theory]
    [InlineData("""{"photoTakenTime": {"timestamp": 1561975200}}""", true)]
    [InlineData("""{"photoTakenTime": {"timestamp": "not a number"}}""", false)]
    [InlineData("""{"photoTakenTime": {"timestamp": "99999999999999999"}}""", false)]
    [InlineData("""{"photoTakenTime": {}}""", false)]
    [InlineData("""{"creationTime": {"timestamp": "1561975200"}}""", false)]
    [InlineData("""{"title": "x"}""", false)]
    public void Takes_a_date_only_from_a_usable_photo_taken_time(string json, bool dated)
    {
        Put("a.jpg");
        Json("a.jpg.json", json);

        Assert.Equal(dated, Read().Find("a.jpg")!.CapturedAt is not null);
    }

    [Fact]
    public void Reads_the_place_from_geo_data_and_falls_back_to_the_exif_one_when_it_is_zero_zero()
    {
        Put("a.jpg");
        Put("b.jpg");
        Put("c.jpg");
        Put("d.jpg");
        Json("a.jpg.json", """{"geoData": {"latitude": 38.7223, "longitude": -9.1393, "altitude": 1.0}, "geoDataExif": {"latitude": 1.0, "longitude": 2.0}}""");
        Json("b.jpg.json", """{"geoData": {"latitude": 0.0, "longitude": 0.0}, "geoDataExif": {"latitude": 41.1579, "longitude": -8.6291}}""");
        Json("c.jpg.json", """{"geoData": {"latitude": 0.0, "longitude": 0.0}}""");
        Json("d.jpg.json", """{"geoData": {"latitude": "38.7", "longitude": "-9.1"}}""");

        var scan = Read();

        Assert.Equal(new GeoPoint(38.7223, -9.1393), scan.Find("a.jpg")!.Location);
        Assert.Equal(new GeoPoint(41.1579, -8.6291), scan.Find("b.jpg")!.Location);
        // 0,0 is carried as read: process counts it as no location (S-045).
        Assert.Null(ExportFacts.Location(scan.Find("c.jpg")));
        Assert.Equal(new GeoPoint(38.7, -9.1), scan.Find("d.jpg")!.Location);
    }

    [Fact]
    public void Reads_the_description_as_the_caption_and_not_the_title()
    {
        Put("a.jpg");
        Put("b.jpg");
        Json("a.jpg.json", """{"title": "IMG_0001.jpg", "description": "Grandma's birthday"}""");
        Json("b.jpg.json", """{"title": "IMG_0002.jpg", "description": ""}""");

        var scan = Read();

        Assert.Equal("Grandma's birthday", scan.Find("a.jpg")!.Caption);
        Assert.Null(scan.Find("b.jpg")!.Caption);
    }

    [Fact]
    public void Names_the_album_after_its_metadata_title_or_its_folder_and_leaves_year_folders_out()
    {
        Put("Photos from 2019/a.jpg");
        Put("Summer in Portugal/a.jpg");
        Put("Weekend/b.jpg");
        Put("Fotos de 2020/c.jpg");
        Json("Summer in Portugal/metadata.json", """{"title": "Summer in Portugal", "description": "", "access": "protected"}""");
        Json("Weekend/metadata.json", """{"title": "  Fim de semana  "}""");

        var scan = Read();

        Assert.Empty(scan.Find("Photos from 2019/a.jpg")!.Albums);
        Assert.Equal(["Summer in Portugal"], scan.Find("Summer in Portugal/a.jpg")!.Albums);
        Assert.Equal(["Fim de semana"], scan.Find("Weekend/b.jpg")!.Albums);
        Assert.Empty(scan.Find("Fotos de 2020/c.jpg")!.Albums);
    }

    [Fact]
    public void Falls_back_to_the_folder_name_without_a_usable_title_and_ignores_the_exports_own_folders()
    {
        Put("Takeout/Google Photos/Trip/a.jpg");
        Put("Takeout/Google Photos/Broken/b.jpg");
        Put("Takeout/Google Photos/c.jpg");
        Json("Takeout/Google Photos/Trip/metadata.json", """{"title": ""}""");
        File.WriteAllText(Path.Combine(_export.Path, "Takeout/Google Photos/Broken/metadata.json"), "{ not json");

        var scan = Read();

        Assert.Equal(["Trip"], scan.Find("Takeout/Google Photos/Trip/a.jpg")!.Albums);
        Assert.Equal(["Broken"], scan.Find("Takeout/Google Photos/Broken/b.jpg")!.Albums);
        Assert.Empty(scan.Find("Takeout/Google Photos/c.jpg")!.Albums);
    }

    [Theory]
    [InlineData("Trash")]
    [InlineData("Lixeira")]
    [InlineData("Papierkorb")]
    [InlineData("Takeout/Google Photos/Corbeille")]
    [InlineData("Takeout/Google Photos/Photos from 2019/Bin")]
    public void Does_not_import_what_is_in_a_trash_folder_whatever_its_language_and_says_so_once(string folder)
    {
        Put($"{folder}/a.jpg");
        Put($"{folder}/b.mp4");
        Put("Trip/c.jpg");
        Json($"{folder}/a.jpg.json", """{"description": "deleted"}""");
        Json("Trip/c.jpg.json", "{}");

        var scan = Read();

        Assert.Equal(1, scan.Count);
        Assert.NotNull(scan.Find("Trip/c.jpg"));
        Assert.True(scan.IsLeftOut($"{folder}/a.jpg"));
        Assert.True(scan.IsLeftOut($"{folder}/b.mp4"));
        Assert.Equal("2 items in Trash were not imported.", Assert.Single(scan.Warnings));
    }

    [Fact]
    public void Prefers_the_sidecars_trashed_flag_to_the_folders_name()
    {
        Put("Trip/gone.jpg");
        Put("Trash/kept.jpg");
        Put("Trash/plain.jpg");
        Json("Trip/gone.jpg.json", """{"trashed": true}""");
        Json("Trash/kept.jpg.json", """{"trashed": false}""");
        Json("Trash/plain.jpg.json", "{}");

        var scan = Read();

        Assert.True(scan.IsLeftOut("Trip/gone.jpg"));
        Assert.NotNull(scan.Find("Trash/kept.jpg"));
        Assert.True(scan.IsLeftOut("Trash/plain.jpg"));
        Assert.Equal("2 items in Trash were not imported.", Assert.Single(scan.Warnings));
    }

    [Fact]
    public void Does_not_count_an_item_in_trash_among_those_without_a_sidecar()
    {
        Put("Trash/a.jpg");

        Assert.Equal("1 items in Trash were not imported.", Assert.Single(Read().Warnings));
    }

    [Theory]
    [InlineData("Archive")]
    [InlineData("Arquivo")]
    [InlineData("Archiv")]
    public void Keeps_archived_photos_in_the_library_without_an_album_of_that_name(string folder)
    {
        Put($"{folder}/a.jpg");
        Json($"{folder}/a.jpg.json", """{"archived": true}""");

        var scan = Read();

        Assert.Empty(scan.Find($"{folder}/a.jpg")!.Albums);
        Assert.False(scan.IsLeftOut($"{folder}/a.jpg"));
    }

    [Fact]
    public void Treats_a_folder_named_like_the_archive_as_an_album_only_when_the_sidecar_says_the_photo_is_not_archived()
    {
        Put("Archive/a.jpg");
        Json("Archive/a.jpg.json", """{"archived": false}""");

        Assert.Equal(["Archive"], Read().Find("Archive/a.jpg")!.Albums);
    }

    [Fact]
    public void Failed_videos_are_not_an_album()
    {
        Put("Failed Videos/a.mp4");
        Json("Failed Videos/a.mp4.json", "{}");

        Assert.Empty(Read().Find("Failed Videos/a.mp4")!.Albums);
    }

    [Theory]
    [InlineData("Takeout")]
    [InlineData("Google Photos")]
    [InlineData("Google Photos (2)")]
    [InlineData("Google Fotos (12)")]
    public void Never_makes_an_album_of_the_exports_own_folders_including_numbered_ones(string folder)
    {
        Put($"{folder}/a.jpg");
        Json($"{folder}/a.jpg.json", "{}");

        Assert.Empty(Read().Find($"{folder}/a.jpg")!.Albums);
    }

    [Fact]
    public void Warns_about_albums_whose_names_have_a_year_so_they_can_be_checked()
    {
        Put("Summer 2019/a.jpg");
        Put("Natal 2018/b.jpg");
        Put("Trip/c.jpg");
        Put("Room 12345/d.jpg");
        foreach (var path in new[] { "Summer 2019/a.jpg", "Natal 2018/b.jpg", "Trip/c.jpg", "Room 12345/d.jpg" })
        {
            Json(path + ".json", "{}");
        }

        var warning = Assert.Single(Read().Warnings);

        Assert.Equal("2 albums look like year folders: Natal 2018, Summer 2019. Check they are albums.", warning);
        Put("Only 2020/e.jpg");
        Json("Only 2020/e.jpg.json", "{}");
        Assert.Contains("3 albums look like year folders", Assert.Single(Read().Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Says_one_album_looks_like_a_year_folder_in_the_singular()
    {
        Put("Summer 2019/a.jpg");
        Json("Summer 2019/a.jpg.json", "{}");

        Assert.Equal("1 album looks like a year folder: Summer 2019. Check it is an album.", Assert.Single(Read().Warnings));
    }

    [Fact]
    public void Gives_every_copy_of_a_file_its_own_folders_album_and_the_same_sidecar_facts()
    {
        Put("Photos from 2019/a.jpg");
        Put("Summer/a.jpg");
        Json("Photos from 2019/a.jpg.json", """{"description": "x"}""");
        Json("Summer/a.jpg.json", """{"description": "x"}""");

        var scan = Read();

        Assert.Empty(scan.Find("Photos from 2019/a.jpg")!.Albums);
        Assert.Equal(["Summer"], scan.Find("Summer/a.jpg")!.Albums);
    }

    [Theory]
    [InlineData("https://photos.google.com/photo/AF1Qip-example", "https://photos.google.com/photo/AF1Qip-example")]
    [InlineData("http://photos.google.com/photo/x", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("photos.google.com/photo/x", null)]
    [InlineData("https://user:secret@photos.example.com/x", null)]
    [InlineData("", null)]
    public void Keeps_the_url_only_when_it_is_an_https_address(string url, string? expected)
    {
        Put("a.jpg");
        Json("a.jpg.json", System.Text.Json.JsonSerializer.Serialize(new { url }));

        Assert.Equal(expected, ExportFacts.SourceUrl(Read().Find("a.jpg")));
    }

    [Fact]
    public void Warns_once_about_media_without_a_sidecar_and_reads_it_with_no_facts()
    {
        Put("a.jpg");
        Put("b.mp4");
        Put("c.jpg");
        Json("c.jpg.json", """{"description": "x"}""");

        var scan = Read();

        Assert.Equal("2 photos and videos in the export have no JSON sidecar and were read from their own metadata only.", Assert.Single(scan.Warnings));
        Assert.Null(scan.Find("a.jpg")!.CapturedAt);
        Assert.Equal(3, scan.Count);
    }

    [Fact]
    public void Warns_about_a_sidecar_that_cannot_be_read_and_still_describes_the_file()
    {
        Put("a.jpg");
        File.WriteAllText(Path.Combine(_export.Path, "a.jpg.json"), "{ not json");

        var scan = Read();

        Assert.Contains("could not be read", Assert.Single(scan.Warnings), StringComparison.Ordinal);
        Assert.NotNull(scan.Find("a.jpg"));
    }

    [Fact]
    public void Does_not_warn_when_every_file_has_a_sidecar_and_treats_json_files_as_no_media()
    {
        Put("a.jpg");
        Json("a.jpg.json", "{}");
        Json("print-subscriptions.json", "[]");
        Json("shared_album_comments.json", "{}");

        var scan = Read();

        Assert.Empty(scan.Warnings);
        Assert.Equal(1, scan.Count);
    }

    [Fact]
    public void Matches_the_newer_supplemental_metadata_sidecars_and_edited_copies_in_the_scan()
    {
        Put("Trip/IMG_0001.jpg");
        Put("Trip/IMG_0001-edited.jpg");
        Put("Trip/IMG_0001(1).jpg");
        Json("Trip/IMG_0001.jpg.supplemental-metadata.json", """{"description": "original"}""");
        Json("Trip/IMG_0001.jpg.supplemental-metadata(1).json", """{"description": "second"}""");

        var scan = Read();

        Assert.Equal("original", scan.Find("Trip/IMG_0001.jpg")!.Caption);
        Assert.Equal("original", scan.Find("Trip/IMG_0001-edited.jpg")!.Caption);
        Assert.Equal("second", scan.Find("Trip/IMG_0001(1).jpg")!.Caption);
    }

    [Fact]
    public void Reads_several_extracted_parts_merged_into_one_folder_and_nothing_outside_media()
    {
        Put("Takeout/Google Photos/Trip/a.jpg");
        Put("Takeout/Google Photos/Trip/notes.txt");
        Json("Takeout/Google Photos/Trip/a.jpg.json", """{"description": "x"}""");

        Assert.Equal(1, Read().Count);
    }

    [Fact]
    public void Changes_nothing_in_the_export()
    {
        Put("Trip/a.jpg");
        Json("Trip/a.jpg.json", """{"description": "x"}""");
        Json("Trip/metadata.json", """{"title": "Trip"}""");
        var before = Snapshot();

        Read();

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Stops_with_a_message_when_the_folder_does_not_exist()
    {
        var ex = Assert.Throws<ExportReadException>(() => new GoogleTakeoutReader().Read(Path.Combine(_export.Path, "nowhere")));

        Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
    }

    private ExportScan Read() => new GoogleTakeoutReader().Read(_export.Path);

    private void Put(string relative) => _export.Write(relative, [1, 2, 3]);

    private void Json(string relative, string json) => _export.Write(relative, System.Text.Encoding.UTF8.GetBytes(json));

    private List<(string, long, DateTime)> Snapshot() =>
        [.. Directory.GetFiles(_export.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f, new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];
}
