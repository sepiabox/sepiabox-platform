using System.Globalization;

namespace Sepiabox.Worker.Assets;

/// <summary>What <c>assets delete</c> prints (S-057): the assets with their source paths and blobs, and the totals.</summary>
public static class AssetDeletionReport
{
    /// <summary>The dry run, or the start of a real run: every asset that matches, with its paths and blobs.</summary>
    public static void WritePlan(DeletionPlan plan, bool dryRun, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteLine(dryRun
            ? $"Dry run: nothing is deleted. {plan.Matched.Count:N0} assets came only from the source '{plan.Source}'."
            : $"{plan.Matched.Count:N0} assets came only from the source '{plan.Source}'.");
        foreach (var asset in plan.Matched)
        {
            writer.WriteLine($"  {asset.Id}{(asset.ManifestMissing ? " (manifest already gone: finishing an interrupted deletion)" : string.Empty)}");
            foreach (var path in asset.SourcePaths)
            {
                writer.WriteLine($"    from {path}");
            }

            foreach (var blob in asset.Blobs)
            {
                writer.WriteLine($"    blob {blob}");
            }
        }

        WriteKept(plan.Kept, "Kept (another source needs them, or their manifest cannot be trusted)", writer);
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Totals: {plan.Matched.Count:N0} assets, {plan.BlobCount:N0} blobs, {plan.OriginalBytes:N0} bytes of originals, {plan.Kept.Count:N0} kept."));
        if (dryRun && plan.Matched.Count > 0)
        {
            writer.WriteLine($"To delete them: assets delete --source \"{plan.Source}\" --yes --expect {plan.Matched.Count}");
        }
    }

    /// <summary>The outcome of a real run.</summary>
    public static void WriteResult(DeletionResult result, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteLine($"Deleted {result.Deleted:N0} of {result.Plan.Matched.Count:N0} assets.");
        WriteKept(result.Kept, "Kept", writer);
        WriteKept(result.ReuploadNeeded, "Re-upload needed (their blobs are gone but a manifest was written again): upload their work folder again", writer);
        if (result.Plan.Matched.Count > 0)
        {
            writer.WriteLine("Occasions still count the deleted assets until: events rebuild");
        }
    }

    private static void WriteKept(IReadOnlyList<KeptAsset> kept, string title, TextWriter writer)
    {
        if (kept.Count == 0)
        {
            return;
        }

        writer.WriteLine($"{title}: {kept.Count:N0}");
        foreach (var asset in kept)
        {
            writer.WriteLine($"  {asset.Id}: {asset.Reason}");
            foreach (var path in asset.SourcePaths)
            {
                writer.WriteLine($"    from {path}");
            }
        }
    }
}
