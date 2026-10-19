using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public class DuplicateFinderTests
{
    [Fact]
    public void Finds_identical_files_but_not_files_that_differ_only_in_the_middle()
    {
        using var folder = new TempFolder();
        var content = RandomBytes(300_000, seed: 1);
        var original = folder.Write("a.jpg", content);
        var copy = folder.Write("copy/b.jpg", content);
        var changedMiddle = (byte[])content.Clone();
        changedMiddle[150_000] ^= 0xFF;
        var edited = folder.Write("c.jpg", changedMiddle);
        var other = folder.Write("d.jpg", RandomBytes(1_000, seed: 2));

        var report = DuplicateFinder.Find(Samples(original, copy, edited, other));

        var group = Assert.Single(report.Groups);
        Assert.Equal([original, copy], group.Paths);
        Assert.Equal(300_000, group.RedundantBytes);
        Assert.Empty(report.Unreadable);
    }

    [Fact]
    public void Compares_small_files_in_full()
    {
        using var folder = new TempFolder();
        var content = RandomBytes(100, seed: 3);
        var first = folder.Write("first.jpg", content);
        var second = folder.Write("second.jpg", content);
        var different = folder.Write("different.jpg", RandomBytes(100, seed: 4));

        var report = DuplicateFinder.Find(Samples(first, second, different));

        Assert.Equal([first, second], Assert.Single(report.Groups).Paths);
    }

    [Fact]
    public void Ignores_empty_files()
    {
        using var folder = new TempFolder();
        var first = folder.Write("first.jpg", []);
        var second = folder.Write("second.jpg", []);

        Assert.Empty(DuplicateFinder.Find(Samples(first, second)).Groups);
    }

    [Fact]
    public void Lists_files_that_cannot_be_read()
    {
        using var folder = new TempFolder();
        var present = folder.Write("present.jpg", RandomBytes(100, seed: 5));
        var missing = Path.Combine(folder.Path, "missing.jpg");

        var report = DuplicateFinder.Find([new FileSample(present, 100), new FileSample(missing, 100)]);

        Assert.Empty(report.Groups);
        Assert.Equal([missing], report.Unreadable);
    }

    private static IEnumerable<FileSample> Samples(params string[] paths) =>
        paths.Select(path => new FileSample(path, new FileInfo(path).Length));

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}
