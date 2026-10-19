using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Scanning;

namespace Sepiabox.Ingest.Tests;

public class EmbeddingWarningsTests
{
    [Fact]
    public void Groups_files_by_reason_and_shows_five_examples_when_many_share_one()
    {
        var problems = Enumerable.Range(1, 7).Select(i => new ScanProblem($"E:\\fotos\\{i}.jpg", "The model returned nothing."))
            .Append(new ScanProblem("E:\\fotos\\x.jpg", "Access is denied."))
            .ToList();
        using var output = new StringWriter();

        EmbeddingWarnings.Write(problems, output);

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [
                "Warning: 8 files have no embedding yet; the next run tries again:",
                "  The model returned nothing. (7 files)",
                "    E:\\fotos\\1.jpg",
                "    E:\\fotos\\2.jpg",
                "    E:\\fotos\\3.jpg",
                "    E:\\fotos\\4.jpg",
                "    E:\\fotos\\5.jpg",
                "    …and 2 more",
                "  Access is denied. (1 files)",
                "    E:\\fotos\\x.jpg",
            ],
            lines);
    }

    [Fact]
    public void Writes_nothing_when_there_are_no_problems()
    {
        using var output = new StringWriter();

        EmbeddingWarnings.Write([], output);

        Assert.Empty(output.ToString());
    }
}
