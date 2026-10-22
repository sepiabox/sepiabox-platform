using System.Text;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Storage;
using Sepiabox.Worker.Sources;

namespace Sepiabox.Worker.Tests.Sources;

/// <summary>What every cloud sync shares (S-038, S-039): the 8 KB metadata limit and how names become path segments.</summary>
public sealed class CloudSyncSupportTests
{
    private static readonly DateTimeOffset s_modified = new(2021, 6, 2, 11, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Stages_metadata_of_exactly_eight_kib_and_stages_nothing_for_one_byte_more(int extra, bool staged)
    {
        var store = new InMemoryBlobStore();
        await store.CreateContainerAsync("incoming");
        var incoming = new IncomingFile("onedrive", "ana@outlook.com (OneDrive)", PathFor(IncomingStager.MaxMetadataBytes + extra), IncomingMode.Reference, s_modified, "id");

        var result = await new IncomingStager(store).StageAsync(incoming, "image/jpeg", (s, ct) => s.WriteAsync(new byte[] { 1, 2, 3 }, ct).AsTask(), CancellationToken.None);

        Assert.Equal(staged, result);
        Assert.Equal(staged ? 1 : 0, await store.ListAsync("incoming").CountAsync());
    }

    [Theory]
    [InlineData("a/b.jpg", "a-b.jpg")]
    [InlineData("a\\b.jpg", "a-b.jpg")]
    [InlineData("  a.jpg ", "a.jpg")]
    public void Makes_a_slash_and_a_backslash_in_a_name_harmless(string name, string expected) =>
        Assert.Equal(expected, CloudNames.FileName(name, "image/jpeg"));

    [Fact]
    public void Makes_a_slash_and_a_backslash_in_a_folder_name_harmless() =>
        Assert.Equal("a-b-c", CloudNames.Segment(" a/b\\c "));

    [Fact]
    public void Gives_a_name_without_an_extension_one_from_its_type_and_leaves_out_what_is_not_media()
    {
        Assert.Equal("IMG.jpg", CloudNames.FileName("IMG", "image/jpeg"));
        Assert.Null(CloudNames.FileName("notes", "application/pdf"));
    }

    private static string PathFor(int metadataBytes)
    {
        for (var length = 1; ; length++)
        {
            var path = new string('p', length) + ".jpg";
            var size = new IncomingFile("onedrive", "ana@outlook.com (OneDrive)", path, IncomingMode.Reference, s_modified, "id")
                .ToMetadata().Sum(p => Encoding.UTF8.GetByteCount(p.Key) + Encoding.UTF8.GetByteCount(p.Value));
            if (size >= metadataBytes)
            {
                Assert.Equal(metadataBytes, size);
                return path;
            }
        }
    }
}
