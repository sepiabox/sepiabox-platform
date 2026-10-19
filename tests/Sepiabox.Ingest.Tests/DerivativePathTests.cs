using Sepiabox.Ingest.Uploading;

namespace Sepiabox.Ingest.Tests;

public sealed class DerivativePathTests
{
    private const string Id = TestManifests.PhotoId;
    private static readonly string s_root = Path.GetPathRoot(Path.GetTempPath())!;
    private static readonly string s_work = Path.Combine(Path.GetTempPath(), "sepiabox-work");

    public static TheoryData<string, string, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, string, bool>
            {
                { s_work, Id, $"derivatives/{Id}/thumb-256.webp", true },
                { s_work + Path.DirectorySeparatorChar, Id, $"derivatives/{Id}/thumb-256.webp", true },
                // --out F:\ or /: the root keeps its separator, which once made every derivative look outside.
                { s_root, Id, $"derivatives/{Id}/thumb-256.webp", true },
                { s_work, Id, Path.Combine(s_root, "secret.webp"), false },
                { s_work, Id, "../escape.webp", false },
                { s_work, Id, "..", false },
                { s_work, Id, $"derivatives/{Id}/../../../secret.webp", false },
                { s_work, Id, $"derivatives/{Id}/..", false },
                { s_work, Id, $"derivatives/{new string('b', 64)}/thumb-256.webp", false },
                { s_work, Id, $"derivatives/{Id}", false },
                { s_work, Id, $"derivatives/{Id}/bad\0name.webp", false },
                // An id that is not one plain segment would move the asset folder itself.
                { s_work, "../..", "secret.webp", false },
                { s_work, "..", "derivatives/secret.webp", false },
                { s_work, "a/b", "derivatives/a/b/thumb-256.webp", false },
                { s_work, @"a\b", @"derivatives/a\b/thumb-256.webp", false },
                { s_work, ".", "derivatives/thumb-256.webp", false },
                { s_work, "", "derivatives/thumb-256.webp", false },
            };
            if (OperatingSystem.IsWindows())
            {
                var otherDrive = s_root.StartsWith('Z') ? @"Y:\" : @"Z:\";
                cases.Add(s_work, Id, $@"derivatives\{Id}\thumb-256.webp", true);
                cases.Add(s_work, Id, $@"derivatives\{Id}\..\..\..\secret.webp", false);
                // Windows paths ignore case, so the same folder spelled differently is still inside.
                cases.Add(s_work.ToUpperInvariant(), Id, $"DERIVATIVES/{Id}/thumb-256.webp", true);
                cases.Add(s_work, Id, otherDrive + "secret.webp", false);
                // Drive-relative: C:x is the current folder on drive C, not inside the work folder.
                cases.Add(s_work, Id, s_root[..2] + "secret.webp", false);
                cases.Add(s_work, Id, @"\secret.webp", false);
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_derivative_must_be_inside_its_own_asset_folder(string workFolder, string assetId, string path, bool inside)
    {
        Assert.Equal(inside, UploadCommand.TryResolveDerivative(workFolder, assetId, path, out _));
    }
}
