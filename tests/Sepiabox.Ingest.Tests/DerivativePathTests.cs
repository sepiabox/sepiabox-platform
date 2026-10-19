using Sepiabox.Ingest.Uploading;

namespace Sepiabox.Ingest.Tests;

public sealed class DerivativePathTests
{
    private const string Id = TestManifests.PhotoId;
    private static readonly string s_root = Path.GetPathRoot(Path.GetTempPath())!;
    private static readonly string s_work = Path.Combine(Path.GetTempPath(), "sepiabox-work");

    public static TheoryData<string, string, bool> Cases => new()
    {
        { s_work, $"derivatives/{Id}/thumb-256.webp", true },
        { s_work + Path.DirectorySeparatorChar, $"derivatives/{Id}/thumb-256.webp", true },
        // --out F:\ or /: the root keeps its separator, which once made every derivative look outside.
        { s_root, $"derivatives/{Id}/thumb-256.webp", true },
        { s_work, Path.Combine(s_root, "secret.webp"), false },
        { s_work, "../escape.webp", false },
        { s_work, $"derivatives/{Id}/../../../secret.webp", false },
        { s_work, $"derivatives/{new string('b', 64)}/thumb-256.webp", false },
        { s_work, $"derivatives/{Id}", false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_derivative_must_be_inside_its_own_asset_folder(string workFolder, string path, bool inside)
    {
        Assert.Equal(inside, UploadCommand.TryResolveDerivative(workFolder, Id, path, out _));
    }
}
