using System.Globalization;
using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Ingest.Tests;

public class ScannerTests
{
    [Fact]
    public void Counts_photos_videos_and_other_files_and_skips_system_folders()
    {
        using var folder = new TempFolder();
        var photo = RandomBytes(2_000, seed: 1);
        folder.Write("2019/beach.jpg", photo);
        folder.Write("backup/beach-copy.jpg", photo);
        folder.Write("2019/party.mov", RandomBytes(5_000, seed: 2));
        folder.Write("notes.txt", RandomBytes(10, seed: 3));
        folder.Write("._beach.jpg", RandomBytes(50, seed: 4));
        folder.Write("$RECYCLE.BIN/deleted.jpg", RandomBytes(3_000, seed: 5));
        folder.Write(".Trashes/old.jpg", RandomBytes(3_000, seed: 6));

        var result = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: true));

        Assert.Equal(new KindTotals(2, 4_000), result.Totals[MediaKind.Photo]);
        Assert.Equal(new KindTotals(1, 5_000), result.Totals[MediaKind.Video]);
        Assert.Equal(new KindTotals(2, 60), result.Totals[MediaKind.Other]);
        Assert.Equal(3, result.MediaByYear[ScanResult.UnknownYear]);
        Assert.Equal(2, Assert.Single(result.Duplicates!).Paths.Count);
        Assert.Equal(1, result.RedundantCopies);
        Assert.Equal(2_000, result.RedundantBytes);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Counts_camcorder_files_as_videos_when_the_extension_is_mod_or_tod()
    {
        using var folder = new TempFolder();
        folder.Write("2008-05-01/MOV001.MOD", RandomBytes(4_000, seed: 7));
        folder.Write("2009-07-12/MOV002.TOD", RandomBytes(6_000, seed: 8));

        var result = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: false));

        Assert.Equal(new KindTotals(2, 10_000), result.Totals[MediaKind.Video]);
        Assert.Equal(new KindTotals(0, 0), result.Totals[MediaKind.Other]);
        Assert.Equal(1, result.FilesByExtension[".mod"]);
        Assert.Equal(1, result.FilesByExtension[".tod"]);
        // No metadata, so their day folders date them (D-046).
        Assert.Equal((1, 1), (result.MediaByYear["2008"], result.MediaByYear["2009"]));
        Assert.False(result.MediaByYear.ContainsKey(ScanResult.UnknownYear));
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Groups_photos_by_the_year_they_were_taken()
    {
        using var folder = new TempFolder();
        var dated = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "taken-2019-07-14.jpg"));
        folder.Write("copied-in-2024/photo.jpg", dated);

        var result = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: false));

        Assert.Equal(1, result.MediaByYear["2019"]);
        Assert.Null(result.Duplicates);
    }

    [Fact]
    public void Counts_an_undated_video_in_the_year_of_its_folder_when_the_folder_is_a_day()
    {
        using var folder = new TempFolder();
        folder.Write("2009-05-01/clip.mpg", RandomBytes(1_000, seed: 7));
        folder.Write("2011-09-25/MNU_110425_d8b2.MTS", RandomBytes(1_000, seed: 8));
        folder.Write("2011-02-30/clip.mpg", RandomBytes(1_000, seed: 9));

        var result = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: false));

        Assert.Equal(1, result.MediaByYear["2009"]);
        Assert.Equal(1, result.MediaByYear["2011"]);
        Assert.Equal(1, result.MediaByYear[ScanResult.UnknownYear]);
    }

    [Fact]
    public void Counts_a_photo_in_the_year_of_its_metadata_when_its_folder_is_another_day()
    {
        using var folder = new TempFolder();
        folder.Write("2005-07-14/praia.jpg", TestPhotos.Porto());
        folder.Write("2005-07-14/reset.jpg", TestPhotos.Taken("1980:01:01 00:00:00"));

        var result = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: false));

        Assert.Equal(1, result.MediaByYear["2019"]);
        Assert.Equal(1, result.MediaByYear["2005"]);
        Assert.False(result.MediaByYear.ContainsKey("1980"));
    }

    [Fact]
    public void Counts_a_damaged_photo_in_the_year_of_its_folder_when_its_format_is_not_recognised()
    {
        using var folder = new TempFolder();
        folder.Write("2005-07-14/damaged.jpg", RandomBytes(500, seed: 11));

        var result = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: false));

        Assert.Equal(1, result.MediaByYear["2005"]);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Counts_a_file_in_the_year_of_its_folder_and_lists_it_when_reading_its_metadata_throws()
    {
        using var folder = new TempFolder();
        folder.Write("2006-01-02/unreadable.jpg", RandomBytes(500, seed: 12));
        folder.Write("2007-03-04/fine.jpg", RandomBytes(500, seed: 13));

        var result = Scanner.Scan(
            new ScanOptions(folder.Path, null, FindDuplicates: false),
            clock: TimeProvider.System,
            readCaptureDate: path => Path.GetFileName(path) == "unreadable.jpg" ? throw new IOException("Device not ready.") : null);

        Assert.Equal(1, result.MediaByYear["2006"]);
        Assert.Equal(1, result.MediaByYear["2007"]);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(("unreadable.jpg", "Device not ready."), (Path.GetFileName(problem.Path), problem.Reason));
    }

    [Fact]
    public void Counts_the_same_years_as_the_dates_process_records_when_given_the_same_files()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("2005-07-14/praia.jpg", TestPhotos.Porto());
        drive.Write("2005-07-14/reset.jpg", TestPhotos.Taken("1980:01:01 00:00:00"));
        drive.Write("1978-08-15/scan.jpg", TestPhotos.Plain());
        drive.Write("2011-09-25/MNU_110425_d8b2.MTS", RandomBytes(800, seed: 14));
        drive.Write("Telemovel/VID_20120304_101112.mp4", RandomBytes(800, seed: 15));
        drive.Write("2011-02-30/clip.mpg", RandomBytes(800, seed: 16));

        var scan = Scanner.Scan(new ScanOptions(drive.Path, null, FindDuplicates: false));
        ProcessCommand.Run(new ProcessOptions(drive.Path, work.Path, "HD", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None));

        var processed = Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json")
            .Select(file => ManifestJson.Deserialize(File.ReadAllText(file)).CapturedAt?.Year.ToString(CultureInfo.InvariantCulture) ?? ScanResult.UnknownYear)
            .GroupBy(year => year)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(processed.OrderBy(pair => pair.Key, StringComparer.Ordinal), scan.MediaByYear);
    }

    [Fact]
    public void Dates_a_scanned_print_from_a_folder_before_1990_when_scanning()
    {
        using var folder = new TempFolder();
        folder.Write("1978-08-15/scan.jpg", TestPhotos.Plain());

        var result = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: false));

        Assert.Equal(1, result.MediaByYear["1978"]);
    }

    [Fact]
    public void Leaves_a_path_date_after_now_out_of_the_years_when_scanning()
    {
        using var folder = new TempFolder();
        folder.Write("2027-01-01/clip.mpg", RandomBytes(1_000, seed: 10));

        var before = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: false), clock: new FixedClock(new DateTime(2026, 10, 20)), readCaptureDate: _ => null);
        var after = Scanner.Scan(new ScanOptions(folder.Path, null, FindDuplicates: false), clock: new FixedClock(new DateTime(2027, 2, 1)), readCaptureDate: _ => null);

        Assert.Equal(1, before.MediaByYear[ScanResult.UnknownYear]);
        Assert.Equal(1, after.MediaByYear["2027"]);
    }

    [Fact]
    public void Fails_clearly_when_the_folder_does_not_exist() =>
        Assert.Throws<DirectoryNotFoundException>(() =>
            Scanner.Scan(new ScanOptions(Path.Combine(Path.GetTempPath(), "sepiabox-missing-" + Guid.NewGuid()), null, true)));

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}
