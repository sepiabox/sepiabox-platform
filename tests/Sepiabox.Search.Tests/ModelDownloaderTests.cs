using System.Net;
using System.Security.Cryptography;
using Sepiabox.Search.Models;

namespace Sepiabox.Search.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private static readonly byte[] s_content = "pretend this is an ONNX model"u8.ToArray();
    private static readonly string s_contentSha256 = Convert.ToHexStringLower(SHA256.HashData(s_content));

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Downloads_and_keeps_a_file_whose_sha256_matches()
    {
        var handler = new ServeBytes(s_content);
        var model = Model(s_contentSha256);

        var outcome = await new ModelDownloader(new HttpClient(handler)).EnsureAsync(model, _folder.Path);

        Assert.Equal(ModelDownloader.Outcome.Downloaded, outcome);
        Assert.Equal(s_content, await File.ReadAllBytesAsync(ModelFolder.PathOf(_folder.Path, model)));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Refuses_a_file_whose_sha256_differs_with_a_clear_message_and_keeps_nothing()
    {
        var pinned = new string('0', 64);
        var model = Model(pinned);

        var failure = await Assert.ThrowsAsync<ModelIntegrityException>(
            () => new ModelDownloader(new HttpClient(new ServeBytes(s_content))).EnsureAsync(model, _folder.Path));

        Assert.Contains(model.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains(s_contentSha256, failure.Message, StringComparison.Ordinal);
        Assert.Contains(pinned, failure.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public async Task Does_not_download_again_a_file_that_is_already_verified()
    {
        var model = Model(s_contentSha256);
        await File.WriteAllBytesAsync(ModelFolder.PathOf(_folder.Path, model), s_content);
        var handler = new ServeBytes(s_content);

        var outcome = await new ModelDownloader(new HttpClient(handler)).EnsureAsync(model, _folder.Path);

        Assert.Equal(ModelDownloader.Outcome.AlreadyPresent, outcome);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Replaces_a_present_file_whose_sha256_differs()
    {
        var model = Model(s_contentSha256);
        await File.WriteAllBytesAsync(ModelFolder.PathOf(_folder.Path, model), "corrupted"u8.ToArray());

        var outcome = await new ModelDownloader(new HttpClient(new ServeBytes(s_content))).EnsureAsync(model, _folder.Path);

        Assert.Equal(ModelDownloader.Outcome.Downloaded, outcome);
        Assert.Equal(s_content, await File.ReadAllBytesAsync(ModelFolder.PathOf(_folder.Path, model)));
    }

    [Fact]
    public async Task Refuses_a_model_with_no_pinned_sha256()
    {
        var handler = new ServeBytes(s_content);

        var failure = await Assert.ThrowsAsync<ModelIntegrityException>(
            () => new ModelDownloader(new HttpClient(handler)).EnsureAsync(Model(""), _folder.Path));

        Assert.Contains("No SHA-256 is pinned", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Downloads_again_when_a_remembered_model_was_corrupted_without_changing_size_or_time()
    {
        var model = Model(s_contentSha256);
        var handler = new ServeBytes(s_content);
        var downloader = new ModelDownloader(new HttpClient(handler));
        await downloader.EnsureAsync(model, _folder.Path);
        var path = ModelFolder.PathOf(_folder.Path, model);
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, [.. s_content.Select(b => (byte)(b ^ 1))]);
        File.SetLastWriteTimeUtc(path, time);

        var outcome = await downloader.EnsureAsync(model, _folder.Path);

        Assert.Equal(ModelDownloader.Outcome.Downloaded, outcome);
        Assert.Equal(s_content, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task Deletes_temporary_files_of_abandoned_downloads_when_they_are_over_an_hour_old()
    {
        var model = Model(s_contentSha256);
        var abandoned = _folder.Write(model.File + ".0123.download", [1, 2, 3]);
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));
        var recent = _folder.Write(model.File + ".4567.download", [4, 5, 6]);
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddMinutes(-30));

        await new ModelDownloader(new HttpClient(new ServeBytes(s_content))).EnsureAsync(model, _folder.Path);

        Assert.False(File.Exists(abandoned));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public async Task Keeps_temporary_files_when_the_inactivity_timeout_is_infinite()
    {
        var model = Model(s_contentSha256);
        var old = _folder.Write(model.File + ".0123.download", [1, 2, 3]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));

        await new ModelDownloader(new HttpClient(new ServeBytes(s_content)), Timeout.InfiniteTimeSpan).EnsureAsync(model, _folder.Path);

        Assert.True(File.Exists(old));
    }

    [Fact]
    public async Task Does_not_report_a_stall_when_progress_reporting_is_slower_than_the_timeout()
    {
        var model = Model(s_contentSha256);
        var slowProgress = new SlowProgress(TimeSpan.FromMilliseconds(400));

        var outcome = await new ModelDownloader(new HttpClient(new ServeBytes(s_content)), TimeSpan.FromMilliseconds(100))
            .EnsureAsync(model, _folder.Path, slowProgress);

        Assert.Equal(ModelDownloader.Outcome.Downloaded, outcome);
    }

    [Fact]
    public async Task Remembers_a_downloaded_model_as_verified()
    {
        var model = Model(s_contentSha256);

        await new ModelDownloader(new HttpClient(new ServeBytes(s_content))).EnsureAsync(model, _folder.Path);

        Assert.True(File.Exists(ModelVerifier.MarkerPath(ModelFolder.PathOf(_folder.Path, model))));
    }

    [Fact]
    public async Task An_error_status_keeps_the_file_that_was_there_and_leaves_no_partial_file()
    {
        var model = Model(s_contentSha256);
        var existing = KeepExisting(model);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => new ModelDownloader(new HttpClient(new ServeBytes(s_content, HttpStatusCode.NotFound))).EnsureAsync(model, _folder.Path));

        AssertOnlyExisting(model, existing);
    }

    [Fact]
    public async Task A_connection_lost_mid_download_keeps_the_file_that_was_there_and_leaves_no_partial_file()
    {
        var model = Model(s_contentSha256);
        var existing = KeepExisting(model);
        var handler = new ServeStream(() => new FailingStream(s_content[..10], new IOException("The connection was reset.")));

        await Assert.ThrowsAsync<IOException>(() => new ModelDownloader(new HttpClient(handler)).EnsureAsync(model, _folder.Path));

        AssertOnlyExisting(model, existing);
    }

    [Fact]
    public async Task A_cancelled_download_keeps_the_file_that_was_there_and_leaves_no_partial_file()
    {
        var model = Model(s_contentSha256);
        var existing = KeepExisting(model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var handler = new ServeStream(() => new FailingStream(s_content[..10], failure: null));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ModelDownloader(new HttpClient(handler)).EnsureAsync(model, _folder.Path, cancellationToken: cancellation.Token));

        AssertOnlyExisting(model, existing);
    }

    [Fact]
    public async Task A_stalled_download_times_out_with_a_clear_message()
    {
        var model = Model(s_contentSha256);
        var handler = new ServeStream(() => new FailingStream(s_content[..10], failure: null));

        var failure = await Assert.ThrowsAsync<TimeoutException>(
            () => new ModelDownloader(new HttpClient(handler), TimeSpan.FromMilliseconds(200)).EnsureAsync(model, _folder.Path));

        Assert.Contains("sent no data", failure.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public void The_pinned_list_has_the_clip_vision_model_with_an_agpl_compatible_license()
    {
        var clip = ModelCatalog.Get(ModelCatalog.ClipVision);

        Assert.Equal("d15189d7028b43f1d3e65039190477f6af591c2a", clip.Revision);
        Assert.Equal($"https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/{clip.Revision}/onnx/vision_model.onnx", clip.Url.ToString());
        Assert.Contains(clip.License, new[] { "MIT", "Apache-2.0", "BSD-3-Clause" });
        Assert.Matches("^[0-9a-f]{64}$", clip.Sha256);
    }

    private static ModelInfo Model(string sha256) =>
        new("test-model", "test-model.onnx", new Uri($"https://models.example/{Revision}/test-model.onnx"), sha256, "MIT", "tests", Revision);

    private const string Revision = "0123456789abcdef0123456789abcdef01234567";

    // A different model already in place, which a failed download must not replace.
    private byte[] KeepExisting(ModelInfo model)
    {
        var existing = "an older model"u8.ToArray();
        File.WriteAllBytes(ModelFolder.PathOf(_folder.Path, model), existing);
        return existing;
    }

    private void AssertOnlyExisting(ModelInfo model, byte[] existing)
    {
        Assert.Equal(existing, File.ReadAllBytes(ModelFolder.PathOf(_folder.Path, model)));
        Assert.DoesNotContain(Directory.GetFiles(_folder.Path), file => file.EndsWith(".download", StringComparison.Ordinal));
    }

    private sealed class ServeBytes(byte[] content, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(content) });
        }
    }

    private sealed class SlowProgress(TimeSpan delay) : IProgress<long>
    {
        public void Report(long value) => Thread.Sleep(delay);
    }

    private sealed class ServeStream(Func<Stream> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) });
    }

    /// <summary>Sends <paramref name="start"/>, then throws <paramref name="failure"/>, or waits forever when it is null.</summary>
    private sealed class FailingStream(byte[] start, Exception? failure) : Stream
    {
        private bool _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                start.CopyTo(buffer);
                return start.Length;
            }

            if (failure is not null)
            {
                throw failure;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
