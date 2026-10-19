using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Storage;

public enum ManifestWriteStatus
{
    Written,
    Unchanged,
    Problem,
}

/// <param name="Status">Whether the manifest was written, was already up to date, or could not be written.</param>
/// <param name="Bytes">Bytes sent, when written.</param>
/// <param name="Problem">Why the manifest was not written, when the status is <see cref="ManifestWriteStatus.Problem"/>.</param>
public sealed record ManifestWriteResult(ManifestWriteStatus Status, long Bytes = 0, string? Problem = null);

/// <summary>
/// The only way to write a manifest blob (D-036): reads what is stored, merges, and writes back only if
/// nobody changed the blob in between. Every writer uses it, so two drives, two computers or the
/// worker never overwrite each other.
/// </summary>
public sealed class ManifestStore(ManifestValidator validator)
{
    public const int MaxAttempts = 5;

    public async Task<ManifestWriteResult> WriteMergedAsync(BlobClient blob, Manifest incoming, CancellationToken cancellationToken)
    {
        // A hand-edited or truncated work-folder manifest (null sources, a width of 0…) must neither
        // throw out of the merge nor reach storage.
        if (!validator.TryValidate(ManifestJson.Serialize(incoming), out _, out var incomingError))
        {
            return Problem($"this manifest is not valid: {incomingError}");
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                // Spread the retries so two writers stuck on the same blob stop colliding.
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(50, 250) * attempt), cancellationToken);
            }

            string? storedJson = null;
            ETag? storedETag = null;
            try
            {
                var current = (await blob.DownloadContentAsync(cancellationToken)).Value;
                storedJson = current.Content.ToString();
                storedETag = current.Details.ETag;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
            }

            Manifest merged;
            if (storedJson is null)
            {
                merged = ManifestMerge.Canonical(incoming);
            }
            else if (!validator.TryValidate(storedJson, out var stored, out var error))
            {
                return Problem($"the manifest in storage cannot be read: {error}");
            }
            else if (stored.Id != incoming.Id)
            {
                return Problem($"the manifest in storage describes asset {stored.Id}, not {incoming.Id}");
            }
            else
            {
                merged = ManifestMerge.Merge(stored, incoming);
            }

            var json = ManifestJson.Serialize(merged);
            if (json == storedJson)
            {
                // Writing would only change the ETag and make the worker import the same thing again.
                return new ManifestWriteResult(ManifestWriteStatus.Unchanged);
            }

            // What is written is checked like what was read: the worker would only reject it later.
            if (!validator.TryValidate(json, out _, out var mergedError))
            {
                return Problem($"the merged manifest is not valid: {mergedError}");
            }

            var content = BinaryData.FromString(json);
            try
            {
                await blob.UploadAsync(
                    content,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                        Conditions = storedETag is { } etag
                            ? new BlobRequestConditions { IfMatch = etag }
                            : new BlobRequestConditions { IfNoneMatch = ETag.All },
                    },
                    cancellationToken);
                return new ManifestWriteResult(ManifestWriteStatus.Written, content.ToMemory().Length);
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                // Someone wrote the manifest between our read and our write: read it again and merge.
            }
        }

        return Problem($"gave up after {MaxAttempts} concurrent changes to the manifest in storage");
    }

    private static ManifestWriteResult Problem(string message) => new(ManifestWriteStatus.Problem, Problem: message);
}
