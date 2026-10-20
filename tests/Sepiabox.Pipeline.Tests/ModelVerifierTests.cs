using System.Security.Cryptography;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Models;

namespace Sepiabox.Pipeline.Tests;

public sealed class ModelVerifierTests : IDisposable
{
    private const string Revision = "0123456789abcdef0123456789abcdef01234567";
    private static readonly byte[] s_garbage = "this is not an ONNX model"u8.ToArray();

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_matching_file_is_verified_and_remembered()
    {
        var model = Model(Sha256(s_garbage));
        var path = _folder.Write(model.File, s_garbage);

        Assert.True(ModelVerifier.Verify(model, path, trustMarker: true, out _, out _));
        Assert.True(File.Exists(ModelVerifier.MarkerPath(path)));
    }

    [Fact]
    public void A_remembered_file_is_not_hashed_again_while_its_size_and_time_are_unchanged()
    {
        var model = Model(Sha256(s_garbage));
        var path = _folder.Write(model.File, s_garbage);
        Assert.True(ModelVerifier.Verify(model, path, trustMarker: true, out _, out _));
        var time = File.GetLastWriteTimeUtc(path);

        // Same length and time, different bytes: only the marker can make this pass.
        File.WriteAllBytes(path, [.. s_garbage.Select(b => (byte)(b ^ 1))]);
        File.SetLastWriteTimeUtc(path, time);

        Assert.True(ModelVerifier.Verify(model, path, trustMarker: true, out _, out _));
    }

    [Fact]
    public void A_changed_file_is_hashed_again_and_refused()
    {
        var model = Model(Sha256(s_garbage));
        var path = _folder.Write(model.File, s_garbage);
        Assert.True(ModelVerifier.Verify(model, path, trustMarker: true, out _, out _));

        File.WriteAllBytes(path, [.. s_garbage, 0]);

        Assert.False(ModelVerifier.Verify(model, path, trustMarker: true, out var actual, out _));
        Assert.Equal(Sha256([.. s_garbage, 0]), actual);
        Assert.False(File.Exists(ModelVerifier.MarkerPath(path)));
    }

    [Fact]
    public void A_garbage_file_in_place_of_the_clip_model_gives_a_warning_to_download_it()
    {
        var model = ModelCatalog.Get(ModelCatalog.ClipVision);
        _folder.Write(model.File, s_garbage);

        using var embedder = ClipImageEmbedder.TryCreate(_folder.Path, out var warning);

        Assert.Null(embedder);
        Assert.Contains("SHA-256", warning, StringComparison.Ordinal);
        Assert.Contains("sepiabox-ingest models download", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_onnx_runtime_cannot_load_gives_a_warning_to_download_it()
    {
        var model = Model(Sha256(s_garbage));
        _folder.Write(model.File, s_garbage);

        using var embedder = ClipImageEmbedder.TryCreate(_folder.Path, model, out var warning);

        Assert.Null(embedder);
        Assert.Contains("ONNX Runtime could not load", warning, StringComparison.Ordinal);
        Assert.Contains("sepiabox-ingest models download", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Hashes_again_when_the_marker_must_not_be_trusted()
    {
        var model = Model(Sha256(s_garbage));
        var path = _folder.Write(model.File, s_garbage);
        Assert.True(ModelVerifier.Verify(model, path, trustMarker: true, out _, out _));
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, [.. s_garbage.Select(b => (byte)(b ^ 1))]);
        File.SetLastWriteTimeUtc(path, time);

        Assert.False(ModelVerifier.Verify(model, path, trustMarker: false, out _, out var fromMarker));
        Assert.False(fromMarker);
    }

    [Fact]
    public void Removes_the_marker_when_a_model_that_matches_its_pin_cannot_be_loaded()
    {
        var model = Model(Sha256(s_garbage));
        var path = _folder.Write(model.File, s_garbage);

        using var embedder = ClipImageEmbedder.TryCreate(_folder.Path, model, out var warning);

        Assert.Null(embedder);
        Assert.False(File.Exists(ModelVerifier.MarkerPath(path)));
        Assert.Contains("verification was removed", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_model_gives_a_warning_to_download_it()
    {
        using var embedder = ClipImageEmbedder.TryCreate(_folder.Path, out var warning);

        Assert.Null(embedder);
        Assert.Contains("is not in", warning, StringComparison.Ordinal);
        Assert.Contains("sepiabox-ingest models download", warning, StringComparison.Ordinal);
    }

    private static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static ModelInfo Model(string sha256) =>
        new("test-model", "test-model.onnx", new Uri($"https://models.example/{Revision}/test-model.onnx"), sha256, "MIT", "tests", Revision);
}
