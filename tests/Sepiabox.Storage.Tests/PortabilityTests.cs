namespace Sepiabox.Storage.Tests;

/// <summary>
/// The rule of D-051 for blob storage (S-050): the Azure Blob SDK is used by <c>Sepiabox.Storage.Azure</c>, by the
/// composition roots and, until S-051, by <c>upload</c>; the rest of the code asks <see cref="IBlobStore"/>.
/// </summary>
public sealed class PortabilityTests
{
    // Host wiring that D-051 counts as composition root, besides each project's Program.cs: the web's data protection key
    // ring in Blob Storage (S-058), which the framework's own extension takes a BlobClient for.
    private static readonly string[] s_hostWiring = ["Program.cs", "AzureSetup.cs"];

    [Theory]
    [InlineData("Sepiabox.Pipeline")]
    [InlineData("Sepiabox.Worker")]
    [InlineData("Sepiabox.Web")]
    public void No_source_file_outside_the_composition_root_uses_an_azure_storage_namespace(string project)
    {
        var offending = SourceFiles(project)
            .Where(file => !s_hostWiring.Contains(Path.GetFileName(file), StringComparer.Ordinal) || Path.GetDirectoryName(file) != ProjectFolder(project))
            .Where(file => File.ReadAllText(file).Contains("Azure.Storage", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void The_web_still_does_not_reference_the_media_pipeline()
    {
        var project = File.ReadAllText(Path.Combine(ProjectFolder("Sepiabox.Web"), "Sepiabox.Web.csproj"));

        Assert.DoesNotContain(@"Sepiabox.Pipeline\Sepiabox.Pipeline.csproj", project, StringComparison.Ordinal);
        Assert.Contains(@"Sepiabox.Storage\Sepiabox.Storage.csproj", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_storage_library_has_no_media_or_database_dependencies()
    {
        var project = File.ReadAllText(Path.Combine(ProjectFolder("Sepiabox.Storage"), "Sepiabox.Storage.csproj"));

        Assert.DoesNotContain("Magick", project, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataExtractor", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
        Assert.DoesNotContain("EntityFramework", project, StringComparison.Ordinal);
    }

    private static string Root { get; } = FindRoot();

    private static string ProjectFolder(string project) => Path.Combine(Root, "src", project);

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(ProjectFolder(project), "*.*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".cs" or ".razor")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sepiabox.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Sepiabox.slnx was not found above the test binaries.");
    }
}
