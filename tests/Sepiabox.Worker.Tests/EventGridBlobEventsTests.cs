using Sepiabox.Worker.Azure;
using Sepiabox.Worker.Events;

namespace Sepiabox.Worker.Tests;

public sealed class EventGridBlobEventsTests
{
    // As Event Grid delivers a storage event to a Service Bus queue (Event Grid schema, one event per message).
    private const string BlobCreatedEvent = """
        {
          "topic": "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-sepiabox/providers/Microsoft.Storage/storageAccounts/storageexample",
          "subject": "/blobServices/default/containers/manifests/blobs/0a1b2c3d4e5f.json",
          "eventType": "Microsoft.Storage.BlobCreated",
          "id": "831e1650-001e-001b-66ab-eeb76e069631",
          "data": {
            "api": "PutBlob",
            "contentType": "application/json",
            "contentLength": 1024,
            "blobType": "BlockBlob",
            "url": "https://storageexample.blob.core.windows.net/manifests/0a1b2c3d4e5f.json"
          },
          "dataVersion": "",
          "metadataVersion": "1",
          "eventTime": "2026-10-20T12:00:00.0000000Z"
        }
        """;

    [Fact]
    public void Extracts_the_container_and_manifest_name_from_a_blob_created_event()
    {
        Assert.Equal(new BlobCreated("manifests", "0a1b2c3d4e5f.json"), EventGridBlobEvents.Parse(BinaryData.FromString(BlobCreatedEvent)));
    }

    [Fact]
    public void Extracts_the_event_when_it_comes_as_an_array_of_one()
    {
        Assert.Equal(
            new BlobCreated("manifests", "0a1b2c3d4e5f.json"),
            EventGridBlobEvents.Parse(BinaryData.FromString("[" + BlobCreatedEvent + "]")));
    }

    [Fact]
    public void Reports_the_other_container_of_an_event_from_another_container()
    {
        var other = BlobCreatedEvent.Replace("/containers/manifests/", "/containers/originals/", StringComparison.Ordinal);

        Assert.Equal(new BlobCreated("originals", "0a1b2c3d4e5f.json"), EventGridBlobEvents.Parse(BinaryData.FromString(other)));
    }

    [Theory]
    [InlineData("/blobServices/default/containers/derivatives/blobs/abc/thumb-256.webp", "derivatives", "abc/thumb-256.webp")]
    [InlineData("/blobServices/default/containers/manifests/blobs/a/blobs/b.json", "manifests", "a/blobs/b.json")]
    public void Keeps_slashes_in_blob_names(string subject, string container, string name)
    {
        Assert.Equal(new BlobCreated(container, name), EventGridBlobEvents.ParseSubject(subject));
    }

    [Theory]
    [InlineData("""{"eventType": "Microsoft.Storage.BlobDeleted", "subject": "/blobServices/default/containers/manifests/blobs/a.json"}""")]
    [InlineData("""{"eventType": "Microsoft.Storage.BlobCreated"}""")]
    [InlineData("""{"eventType": "Microsoft.Storage.BlobCreated", "subject": "/blobServices/default/containers/manifests"}""")]
    [InlineData("""{"eventType": "Microsoft.Storage.BlobCreated", "subject": "/blobServices/default/containers/manifests/blobs/"}""")]
    [InlineData("""{"eventType": "Microsoft.Storage.BlobCreated", "subject": "/other/manifests/blobs/a.json"}""")]
    [InlineData("""{"eventType": 5, "subject": "/blobServices/default/containers/manifests/blobs/a.json"}""")]
    [InlineData("""[]""")]
    [InlineData("""["a", "b"]""")]
    [InlineData("""not json""")]
    [InlineData("""42""")]
    public void Returns_nothing_for_a_message_that_is_not_a_usable_blob_created_event(string body)
    {
        Assert.Null(EventGridBlobEvents.Parse(BinaryData.FromString(body)));
    }
}
