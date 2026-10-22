using Sepiabox.Storage;
using Sepiabox.Worker.Exports;

namespace Sepiabox.Worker.Tests.Exports;

public sealed class StagedUploadStreamTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(StagedUploadStream.BlockSize)]
    [InlineData((StagedUploadStream.BlockSize * 2) + 7)]
    public async Task Writes_what_it_is_given_as_blocks_and_the_last_partial_one(int size)
    {
        var store = new InMemoryBlobStore();
        store.CreateContainer("exports");
        var bytes = new byte[size];
        new Random(5).NextBytes(bytes);
        await using var upload = store.BeginUpload("exports", "a.bin");

        await using (var stream = new StagedUploadStream(upload))
        {
            // In uneven pieces, as an archive writes.
            for (var offset = 0; offset < size; offset += 100_000)
            {
                stream.Write(bytes, offset, Math.Min(100_000, size - offset));
            }

            Assert.Equal(size, stream.Length);
            await stream.FinishAsync();
        }

        Assert.Null(await store.ReadAsync("exports", "a.bin"));
        await upload.CommitAsync("application/zip");
        Assert.Equal(bytes, (await store.ReadAsync("exports", "a.bin"))!.Content.ToArray());
    }

    [Fact]
    public void Cannot_be_read_or_sought()
    {
        using var stream = new StagedUploadStream(new InMemoryBlobStore().BeginUpload("exports", "a"));

        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }
}
