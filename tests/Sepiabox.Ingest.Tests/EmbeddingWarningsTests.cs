using Sepiabox.Ingest.Processing;

namespace Sepiabox.Ingest.Tests;

public class EmbeddingWarningsTests
{
    private const string Unreadable = "Could not compute the clip-vit-b32 embedding (MagickCorruptImageErrorException)";

    [Fact]
    public void Groups_files_by_category_when_every_file_has_its_own_message()
    {
        var problems = Enumerable.Range(1, 7)
            .Select(i => new EmbeddingProblem($"E:\\fotos\\{i}.jpg", Unreadable, $"Could not compute the clip-vit-b32 embedding: corrupt image `{i}.webp'"))
            .Append(new EmbeddingProblem("E:\\fotos\\x.jpg", "Could not read its manifest (JsonException)", "Could not read its manifest: line 3"))
            .ToList();
        using var output = new StringWriter();

        EmbeddingWarnings.Write(problems, output);

        Assert.Equal(
            [
                "Warning: 8 files have no embedding yet; the next run tries again:",
                $"  {Unreadable}: 7 files",
                "    for example: Could not compute the clip-vit-b32 embedding: corrupt image `1.webp'",
                "    E:\\fotos\\1.jpg",
                "    E:\\fotos\\2.jpg",
                "    E:\\fotos\\3.jpg",
                "    E:\\fotos\\4.jpg",
                "    E:\\fotos\\5.jpg",
                "    …and 2 more",
                "  Could not read its manifest (JsonException): 1 file",
                "    for example: Could not read its manifest: line 3",
                "    E:\\fotos\\x.jpg",
            ],
            Lines(output));
    }

    [Fact]
    public void Shows_ten_reasons_and_counts_the_rest_when_there_are_more()
    {
        var problems = Enumerable.Range(1, 13).Select(i => new EmbeddingProblem($"{i}.jpg", $"Reason {i:D2}", "detail")).ToList();
        using var output = new StringWriter();

        EmbeddingWarnings.Write(problems, output);

        var lines = Lines(output);
        Assert.Equal(10, lines.Count(line => line.StartsWith("  Reason", StringComparison.Ordinal)));
        Assert.Equal("  …and 3 other reasons (3 files)", lines[^1]);
    }

    [Fact]
    public void Writes_nothing_when_there_are_no_problems()
    {
        using var output = new StringWriter();

        EmbeddingWarnings.Write([], output);

        Assert.Empty(output.ToString());
    }

    private static string[] Lines(StringWriter output) =>
        output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
