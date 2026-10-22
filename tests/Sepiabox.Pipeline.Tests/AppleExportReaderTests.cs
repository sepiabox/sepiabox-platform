using System.Text;
using Sepiabox.Pipeline.Exports;

namespace Sepiabox.Pipeline.Tests;

/// <summary>
/// The Apple Photos reader (S-047, D-109) over synthetic exports generated here: only names, CSV and XMP text, no photos,
/// and no real person's data. The CSV layout and date format are believed, not confirmed (D-109).
/// </summary>
public sealed class AppleExportReaderTests : IDisposable
{
    private const string Header = "imgName,fileChecksum,favorite,hidden,deleted,originalCreationDate,viewCount,importDate";
    private readonly TempFolder _export = new();

    public void Dispose() => _export.Dispose();

    [Fact]
    public void Reads_the_date_of_a_details_row_as_utc_and_leaves_a_file_with_no_row_without_facts()
    {
        Put("Photos/IMG_0001.HEIC");
        Put("Photos/IMG_0002.HEIC");
        Text("Photo Details.csv", $"{Header}\nIMG_0001.HEIC,abc=,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",1,\"Thursday May 24,2018 9:00 AM GMT\"\n");

        var scan = Read();

        Assert.Equal(new DateTime(2018, 5, 23, 16, 16, 0, DateTimeKind.Utc), scan.Find("Photos/IMG_0001.HEIC")!.CapturedAt);
        Assert.Null(scan.Find("Photos/IMG_0002.HEIC")!.CapturedAt);
        Assert.Contains(scan.Warnings, w => w.Contains("1 photos and videos have no row in Photo Details", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Wednesday May 23,2018 4:16 PM GMT", "2018-05-23T16:16:00", DateTimeKind.Utc)]
    [InlineData("Wednesday May 23, 2018 4:16 PM GMT", "2018-05-23T16:16:00", DateTimeKind.Utc)]
    [InlineData("Friday January 5,2018 12:05 AM GMT", "2018-01-05T00:05:00", DateTimeKind.Utc)]
    [InlineData("Saturday December 1,2018 11:59:30 PM UTC", "2018-12-01T23:59:30", DateTimeKind.Utc)]
    [InlineData("Wednesday May 23,2018 16:16", "2018-05-23T16:16:00", DateTimeKind.Unspecified)]
    [InlineData("Tuesday May 23,2018 4:16 PM GMT", "2018-05-23T16:16:00", DateTimeKind.Utc)]
    [InlineData("May 23, 2018 4:16 PM", "2018-05-23T16:16:00", DateTimeKind.Unspecified)]
    [InlineData("2018-05-23T16:16:00Z", "2018-05-23T16:16:00", DateTimeKind.Utc)]
    [InlineData("  Wednesday   May 23,2018 4:16 PM   GMT ", "2018-05-23T16:16:00", DateTimeKind.Utc)]
    public void Parses_the_forms_of_original_creation_date(string text, string expected, DateTimeKind kind)
    {
        var date = AppleExportReader.ParseDate(text);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date);
        Assert.Equal(kind, date!.Value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("23/05/2018 16:16")]
    [InlineData("05/23/2018 4:16 PM")]
    [InlineData("Wednesday Smarch 23,2018 4:16 PM GMT")]
    [InlineData("whenever")]
    public void Parses_no_date_from_a_form_it_does_not_know(string text) => Assert.Null(AppleExportReader.ParseDate(text));

    [Fact]
    public void Warns_once_about_dates_in_an_unknown_form_and_still_reads_the_rest()
    {
        Put("a.heic");
        Put("b.heic");
        Text("Photo Details.csv", $"{Header}\na.heic,x,N,N,N,23/05/2018 16:16,0,\nb.heic,x,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",0,\n");

        var scan = Read();

        Assert.Null(scan.Find("a.heic")!.CapturedAt);
        Assert.NotNull(scan.Find("b.heic")!.CapturedAt);
        var warning = Assert.Single(scan.Warnings, w => w.Contains("not in a known date format", StringComparison.Ordinal));
        Assert.Contains("1 originalCreationDate", warning, StringComparison.Ordinal);
        Assert.Contains("23/05/2018 16:16", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_out_a_file_marked_deleted_and_lists_it()
    {
        Put("Photos/IMG_0001.HEIC");
        Put("Photos/IMG_0002.HEIC");
        Put("Photos/IMG_0003.HEIC");
        Text("Photo Details.csv", $"{Header}\nIMG_0001.HEIC,a,N,N,Y,,0,\nIMG_0002.HEIC,b,N,N,N,,0,\nIMG_0003.HEIC,c,N,N,Recently Deleted,,0,\n");

        var scan = Read();

        Assert.True(scan.IsLeftOut("Photos/IMG_0001.HEIC"));
        Assert.True(scan.IsLeftOut("Photos/IMG_0003.HEIC"));
        Assert.False(scan.IsLeftOut("Photos/IMG_0002.HEIC"));
        Assert.Null(scan.Find("Photos/IMG_0001.HEIC"));
        Assert.Contains(scan.Warnings, w => w.Contains("2 photos and videos are marked deleted", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("N", false)]
    [InlineData("no", false)]
    [InlineData("FALSE", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("Y", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("May 1,2018", true)]
    public void Takes_any_deleted_value_that_is_not_a_plain_no_as_deleted(string value, bool leftOut)
    {
        Put("a.heic");
        Text("Photo Details.csv", $"{Header}\na.heic,x,N,N,{value},,0,\n");

        Assert.Equal(leftOut, Read().IsLeftOut("a.heic"));
    }

    [Fact]
    public void Finds_the_columns_by_name_in_any_order_and_ignores_the_case_of_the_header_and_of_file_names()
    {
        Put("Photos/img_0001.heic");
        Text("Photo Details-2.csv", "importDate,ORIGINALCREATIONDATE,Img Name,deleted\n,\"Wednesday May 23,2018 4:16 PM GMT\",IMG_0001.HEIC,N\n");

        var scan = Read();

        Assert.Equal(new DateTime(2018, 5, 23, 16, 16, 0, DateTimeKind.Utc), scan.Find("Photos/img_0001.heic")!.CapturedAt);
    }

    [Fact]
    public void Reads_names_with_commas_and_quotes_and_in_either_unicode_form()
    {
        Put("Photos/Café, no. 1.HEIC");
        Text("Photo Details.csv", $"{Header}\n\"Café, no. 1.HEIC\",x,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",0,\n");

        Assert.NotNull(Read().Find("Photos/Café, no. 1.HEIC")!.CapturedAt);
    }

    [Fact]
    public void Ignores_details_rows_that_disagree_and_names_that_two_files_share()
    {
        Put("One/IMG_0001.HEIC");
        Put("Two/IMG_0001.HEIC");
        Put("IMG_0002.HEIC");
        Text("Photo Details.csv", $"{Header}\nIMG_0001.HEIC,a,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",0,\nIMG_0002.HEIC,b,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",0,\nIMG_0002.HEIC,b,N,N,Y,\"Wednesday May 23,2018 4:16 PM GMT\",0,\n");

        var scan = Read();

        Assert.Null(scan.Find("One/IMG_0001.HEIC")!.CapturedAt);
        Assert.Null(scan.Find("Two/IMG_0001.HEIC")!.CapturedAt);
        Assert.Null(scan.Find("IMG_0002.HEIC")!.CapturedAt);
        Assert.False(scan.IsLeftOut("IMG_0002.HEIC"));
        Assert.Contains(scan.Warnings, w => w.Contains("2 photos and videos share a file name", StringComparison.Ordinal));
    }

    [Fact]
    public void Reads_a_row_repeated_exactly_in_two_files_as_one()
    {
        Put("a.heic");
        var row = "a.heic,x,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",0,\n";
        Text("Photo Details.csv", Header + "\n" + row);
        Text("Photo Details-2.csv", Header + "\n" + row);

        Assert.NotNull(Read().Find("a.heic")!.CapturedAt);
    }

    [Fact]
    public void Warns_about_a_details_file_without_an_image_name_column()
    {
        Put("a.heic");
        Text("Photo Details.csv", "name,date\na.heic,x\n");

        var scan = Read();

        Assert.Null(scan.Find("a.heic")!.CapturedAt);
        Assert.Contains(scan.Warnings, w => w.Contains("Photo Details.csv has no imgName column", StringComparison.Ordinal));
    }

    [Fact]
    public void Takes_the_album_from_the_name_of_each_csv_in_the_albums_folder()
    {
        Put("Photos/IMG_0001.HEIC");
        Put("Photos/IMG_0002.HEIC");
        Put("Photos/IMG_0003.HEIC");
        Text("Albums/Holidays.csv", "Images\nIMG_0001.HEIC\nIMG_0002.HEIC\nIMG_9999.HEIC\n");
        Text("Albums/Família, 2018.csv", "Images\nIMG_0002.HEIC\n");

        var scan = Read();

        Assert.Equal(["Holidays"], scan.Find("Photos/IMG_0001.HEIC")!.Albums);
        Assert.Equal(["Família, 2018", "Holidays"], scan.Find("Photos/IMG_0002.HEIC")!.Albums);
        Assert.Empty(scan.Find("Photos/IMG_0003.HEIC")!.Albums);
    }

    [Fact]
    public void Warns_about_an_album_list_that_names_no_file()
    {
        Put("a.heic");
        Text("Albums/Empty.csv", "Images\nzzz.heic\n");

        Assert.Contains(Read().Warnings, w => w.Contains("Albums/Empty.csv names no file", StringComparison.Ordinal));
    }

    [Fact]
    public void Does_not_take_a_folder_name_as_an_album()
    {
        Put("2018-05-23 Beach/IMG_0001.HEIC");
        Put("Photos/Moment/IMG_0002.HEIC");

        var scan = Read();

        Assert.Empty(scan.Find("2018-05-23 Beach/IMG_0001.HEIC")!.Albums);
        Assert.Empty(scan.Find("Photos/Moment/IMG_0002.HEIC")!.Albums);
    }

    [Fact]
    public void Reads_the_xmp_sidecar_named_after_the_file_or_after_its_stem()
    {
        Put("IMG_0001.HEIC");
        Put("IMG_0002.JPG");
        Put("IMG_0003.JPG");
        Put("IMG_0003.MOV");
        Xmp("IMG_0001.HEIC.xmp", "One");
        Xmp("img_0002.xmp", "Two");
        Xmp("IMG_0003.xmp", "Three");

        var scan = Read();

        Assert.Equal("One", scan.Find("IMG_0001.HEIC")!.Caption);
        Assert.Equal("Two", scan.Find("IMG_0002.JPG")!.Caption);
        Assert.Equal("Three", scan.Find("IMG_0003.JPG")!.Caption);
        Assert.Equal("Three", scan.Find("IMG_0003.MOV")!.Caption);
    }

    [Fact]
    public void Prefers_the_file_name_sidecar_to_the_stem_one()
    {
        Put("IMG_0001.HEIC");
        Xmp("IMG_0001.xmp", "Stem");
        Xmp("IMG_0001.HEIC.xmp", "Exact");

        Assert.Equal("Exact", Read().Find("IMG_0001.HEIC")!.Caption);
    }

    [Fact]
    public void Prefers_the_xmp_date_to_the_csv_one_and_takes_the_xmp_position()
    {
        Put("a.heic");
        Text("Photo Details.csv", $"{Header}\na.heic,x,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",0,\n");
        _export.Write("a.heic.xmp", Encoding.UTF8.GetBytes(XmpText("<photoshop:DateCreated>2017-01-02T03:04:05</photoshop:DateCreated><exif:GPSLatitude>38,43.338N</exif:GPSLatitude><exif:GPSLongitude>9,8.358W</exif:GPSLongitude>")));

        var facts = Read().Find("a.heic")!;

        Assert.Equal(new DateTime(2017, 1, 2, 3, 4, 5), facts.CapturedAt);
        Assert.Equal(38.7223, facts.Location!.Latitude, 4);
    }

    [Fact]
    public void Warns_about_an_unreadable_xmp_and_still_reads_the_file()
    {
        Put("a.heic");
        _export.Write("a.heic.xmp", Encoding.UTF8.GetBytes("not xml"));

        var scan = Read();

        Assert.NotNull(scan.Find("a.heic"));
        Assert.Contains(scan.Warnings, w => w.Contains("1 .xmp sidecars could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaves_edit_sidecars_and_other_files_out_of_the_media()
    {
        Put("IMG_0001.HEIC");
        _export.Write("IMG_O0001.AAE", [1]);
        _export.Write("notes.txt", [1]);

        var scan = Read();

        Assert.Equal(1, scan.Count);
        Assert.Empty(scan.Warnings);
    }

    [Fact]
    public void Reads_both_layouts_together_and_changes_no_file()
    {
        Put("Photos/IMG_0001.HEIC");
        Text("Photo Details.csv", $"{Header}\nIMG_0001.HEIC,x,N,N,N,\"Wednesday May 23,2018 4:16 PM GMT\",0,\n");
        Text("Albums/Holidays.csv", "Images\nIMG_0001.HEIC\n");
        Xmp("Photos/IMG_0001.HEIC.xmp", "Beach");
        var before = Snapshot();

        var facts = Read().Find("Photos/IMG_0001.HEIC")!;

        Assert.Equal("Beach", facts.Caption);
        Assert.Equal(["Holidays"], facts.Albums);
        Assert.NotNull(facts.CapturedAt);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Throws_when_the_folder_does_not_exist()
    {
        Assert.Throws<ExportReadException>(() => new AppleExportReader().Read(Path.Combine(_export.Path, "missing")));
    }

    [Fact]
    public void Is_registered_as_apple_photos()
    {
        Assert.True(ExportReaderRegistry.Default.TryGet("apple-photos", out var reader));
        Assert.IsType<AppleExportReader>(reader);
    }

    private ExportScan Read() => new AppleExportReader().Read(_export.Path);

    private void Put(string relative) => _export.Write(relative, [1, 2, 3]);

    private void Text(string relative, string text) => _export.Write(relative, Encoding.UTF8.GetBytes(text));

    private void Xmp(string relative, string description) =>
        Text(relative, XmpText($"<dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">{description}</rdf:li></rdf:Alt></dc:description>"));

    private static string XmpText(string properties) =>
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:photoshop=\"http://ns.adobe.com/photoshop/1.0/\" xmlns:exif=\"http://ns.adobe.com/exif/1.0/\">"
        + properties + "</rdf:Description></rdf:RDF></x:xmpmeta>";

    private List<(string, long, DateTime)> Snapshot() =>
        [.. Directory.GetFiles(_export.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => (f, new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))];
}
