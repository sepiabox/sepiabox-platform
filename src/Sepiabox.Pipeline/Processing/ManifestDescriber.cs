using Sepiabox.Pipeline.Exports;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Processing;

/// <summary>
/// What a file is, as the manifest says it (S-065, D-118): the one place that turns a file's own metadata, its path and what an
/// export said into a manifest's kind, size, content type, capture date, place, camera and dimensions. <c>process</c> (a
/// folder) and the cloud worker (one file from <c>incoming</c>) both describe through it, so a rule changed here changes both.
/// </summary>
public static class ManifestDescriber
{
    /// <summary>A new manifest for a file.</summary>
    /// <param name="Sha256">The content's hash, which is the asset's id.</param>
    /// <param name="Kind">Photo or video.</param>
    /// <param name="SizeBytes">The file's size.</param>
    /// <param name="Path">
    /// The file's path, from which the content type (its extension) and a date (its name and folders, D-046) come: the real
    /// path on a PC, the cloud source's path for a file that arrived in <c>incoming</c>.
    /// </param>
    /// <param name="Metadata">What <see cref="MetadataReader"/> read from the file's content.</param>
    /// <param name="Source">The one source the manifest starts with.</param>
    /// <param name="IndexOnly">Whether the original is not kept (D-088): written as <c>originalStored: false</c>, and only then.</param>
    /// <param name="Clock">For the path dates (D-046).</param>
    /// <param name="Export">What an export said about the file (S-045); null for none.</param>
    /// <param name="ExportZone">The zone the export's UTC times are shown in (D-037); required with <paramref name="Export"/>.</param>
    public sealed record Request(
        string Sha256, MediaKind Kind, long SizeBytes, string Path, MediaMetadata Metadata, ManifestSource Source, bool IndexOnly,
        TimeProvider Clock, ExportMetadata? Export = null, TimeZoneInfo? ExportZone = null);

    public static Manifest Describe(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exportDate = request.Export is null ? null : ExportFacts.WallClock(request.Export, request.ExportZone ?? TimeZoneInfo.Utc);
        var exportLocation = ExportFacts.Location(request.Export);
        var metadata = request.Metadata;
        return new Manifest
        {
            Id = request.Sha256,
            Kind = request.Kind,
            SizeBytes = request.SizeBytes,
            ContentType = MediaTypes.ContentType(request.Path),
            // The file's own date when plausible, otherwise one from its path (D-046), or the export's (S-045).
            Captured = CaptureDate.Resolve(metadata.RawCapturedAt, request.Path, request.Clock, exportDate),
            // The file's own place wins; the export's fills in only when the file has none (S-045, D-104).
            Location = metadata.Location ?? exportLocation,
            Caption = ExportFacts.Caption(request.Export),
            Albums = ExportFacts.Albums(request.Export),
            Camera = metadata.Camera,
            Width = metadata.Width,
            Height = metadata.Height,
            DurationSeconds = metadata.DurationSeconds,
            Sources = [request.Source],
            // Written only when false, as the schema says; a work folder keeps one mode, so every manifest in it agrees.
            OriginalStored = request.IndexOnly ? false : null,
        };
    }
}
