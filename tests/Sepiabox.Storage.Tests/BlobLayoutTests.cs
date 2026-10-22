namespace Sepiabox.Storage.Tests;

public sealed class BlobLayoutTests
{
    private const string Id = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";

    [Theory]
    [InlineData($"derivatives/{Id}/thumb-256.webp")]
    [InlineData($"derivatives\\{Id}\\thumb-256.webp")]
    [InlineData("thumb-256.webp")]
    public void Derivative_blob_name_is_the_file_name_under_the_asset_id_on_every_platform(string path)
    {
        Assert.Equal($"{Id}/thumb-256.webp", BlobLayout.DerivativeBlobName(Id, path));
    }

    [Fact]
    public void Manifest_blob_name_is_the_asset_id_with_json()
    {
        Assert.Equal($"{Id}.json", BlobLayout.ManifestBlobName(Id));
    }

    [Fact]
    public void The_containers_are_the_ones_upload_writes_and_the_deploy_creates()
    {
        // Named in the AppHost and in operations.md too: a change here is a change to the deployed storage.
        Assert.Equal(["originals", "derivatives", "manifests"], [BlobLayout.OriginalsContainer, BlobLayout.DerivativesContainer, BlobLayout.ManifestsContainer]);
    }
}
