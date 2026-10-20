using System.Text.Json;
using Sepiabox.Worker.Events;

namespace Sepiabox.Worker.Azure;

/// <summary>
/// Reads the Event Grid event a Service Bus message carries (Event Grid schema, one event per message):
/// <c>Microsoft.Storage.BlobCreated</c> with the subject <c>/blobServices/default/containers/&lt;container&gt;/blobs/&lt;name&gt;</c>.
/// </summary>
public static class EventGridBlobEvents
{
    public const string BlobCreatedType = "Microsoft.Storage.BlobCreated";

    private const string ContainersPrefix = "/blobServices/default/containers/";
    private const string BlobsSegment = "/blobs/";

    /// <summary>The event, or null when the body is not a blob-created event with a usable subject.</summary>
    public static BlobCreated? Parse(BinaryData body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            // Event Grid can also deliver an array; a Service Bus destination gets one event per message.
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() != 1)
                {
                    return null;
                }

                root = root[0];
            }

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("eventType", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() != BlobCreatedType
                || !root.TryGetProperty("subject", out var subject) || subject.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return ParseSubject(subject.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Container and blob name from a subject; the blob name may itself contain <c>/</c>.</summary>
    public static BlobCreated? ParseSubject(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!subject.StartsWith(ContainersPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = subject[ContainersPrefix.Length..];
        var blobs = rest.IndexOf(BlobsSegment, StringComparison.Ordinal);
        if (blobs <= 0)
        {
            return null;
        }

        var container = rest[..blobs];
        var name = rest[(blobs + BlobsSegment.Length)..];
        return container.Contains('/', StringComparison.Ordinal) || name.Length == 0 ? null : new BlobCreated(container, name);
    }
}
