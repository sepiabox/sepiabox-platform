using System.Net;
using System.Security.Cryptography;
using Sepiabox.Pipeline.Models;

namespace Sepiabox.Pipeline.Tests;

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
    public void The_pinned_list_has_the_clip_vision_model_with_an_agpl_compatible_license()
    {
        var clip = ModelCatalog.Get(ModelCatalog.ClipVision);

        Assert.Equal("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx", clip.Url.ToString());
        Assert.Contains(clip.License, new[] { "MIT", "Apache-2.0", "BSD-3-Clause" });
        Assert.Matches("^[0-9a-f]{64}$", clip.Sha256);
    }

    private static ModelInfo Model(string sha256) =>
        new("test-model", "test-model.onnx", new Uri("https://models.example/test-model.onnx"), sha256, "MIT", "tests");

    private sealed class ServeBytes(byte[] content) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }
}
