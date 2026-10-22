using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Sepiabox.Storage.Azure;

namespace Sepiabox.Storage.Tests;

/// <summary>
/// The access tier a staged upload asks Azure for (S-051, D-004). Azurite does not know the Cold tier, so the request is
/// caught on its way out by a pipeline policy, which sees the header Azure would have received.
/// </summary>
public sealed class AzureStagedUploadTierTests
{
    [Theory]
    [InlineData(BlobStorageClass.Cold, "Cold")]
    [InlineData(BlobStorageClass.Default, null)]
    public async Task Commits_the_block_list_with_the_tier_the_class_names(BlobStorageClass storageClass, string? expectedTier)
    {
        var recorder = new RecordingPolicy();
        var options = new BlobClientOptions();
        options.AddPolicy(recorder, HttpPipelinePosition.PerCall);
        using var store = new AzureBlobStore(new BlobServiceClient("UseDevelopmentStorage=true", options));

        await using var upload = store.BeginUpload("originals", "abc");
        await Assert.ThrowsAsync<RecordedException>(() => upload.CommitAsync("image/jpeg", storageClass));

        Assert.Contains("comp=blocklist", recorder.Uri, StringComparison.Ordinal);
        Assert.Equal(expectedTier, recorder.AccessTier);
        Assert.Equal("image/jpeg", recorder.ContentType);
    }

    private sealed class RecordedException : Exception;

    private sealed class RecordingPolicy : HttpPipelinePolicy
    {
        public string Uri { get; private set; } = "";

        public string? AccessTier { get; private set; }

        public string? ContentType { get; private set; }

        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline) => Record(message);

        public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            Record(message);
            return ValueTask.CompletedTask;
        }

        private void Record(HttpMessage message)
        {
            Uri = message.Request.Uri.ToString();
            AccessTier = message.Request.Headers.TryGetValue("x-ms-access-tier", out var tier) ? tier : null;
            ContentType = message.Request.Headers.TryGetValue("x-ms-blob-content-type", out var type) ? type : null;
            throw new RecordedException();
        }
    }
}
