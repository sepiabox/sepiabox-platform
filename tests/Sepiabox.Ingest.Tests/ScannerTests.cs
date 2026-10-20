using Sepiabox.Ingest.Scanning;
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
        Assert.Equal(2, result.MediaByYear[ScanResult.UnknownYear]);
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
