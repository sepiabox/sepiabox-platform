using System.Globalization;

namespace Sepiabox.Worker.Assets;

/// <summary>What <c>assets delete</c> prints (S-057): the assets with their source paths and blobs, and the totals.</summary>
public static class AssetDeletionReport
{
    /// <summary>The most assets listed one by one; the rest are counted.</summary>
    public const int MaxListed = 200;

    /// <summary>The Cold tier's price in USD per GB-month, rounded up (an estimate to be checked against the price list).</summary>
    public const double ColdUsdPerGbMonth = 0.005;

    /// <summary>The most a deleted original can cost in early-deletion fee: the Cold tier's 90 days, 3 months, of its storage.</summary>
    public static double ColdEarlyDeletionUsd(long bytes) => bytes / 1_073_741_824d * ColdUsdPerGbMonth * 3;

    /// <summary>The dry run, or the start of a real run: every asset that matches, with its paths and blobs.</summary>
    public static void WritePlan(DeletionPlan plan, bool dryRun, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(writer);
        // The totals come first: a long listing may be cut by the log's daily cap, and the numbers are what is decided on.
        writer.WriteLine(dryRun
            ? $"Dry run: nothing is deleted. {plan.Matched.Count:N0} assets came only from the source '{plan.Source}'."
            : $"{plan.Matched.Count:N0} assets came only from the source '{plan.Source}'.");
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Totals: {plan.Matched.Count:N0} assets, {plan.BlobCount:N0} blobs, {plan.OriginalBytes:N0} bytes of originals, {plan.Kept.Count:N0} kept."));
        writer.WriteLine($"Source type: {(plan.SourceTypes.Count == 0 ? "none" : string.Join(", ", plan.SourceTypes))}");
        if (plan.OriginalBytes > 0)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Cold tier early-deletion fee: up to about USD {ColdEarlyDeletionUsd(plan.OriginalBytes):N2} if the originals are new (rough: {ColdUsdPerGbMonth} USD per GB-month for up to 3 months; check the current price)."));
        }

        foreach (var asset in plan.Matched.Take(MaxListed))
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

        if (plan.Matched.Count > MaxListed)
        {
            writer.WriteLine($"  ... and {plan.Matched.Count - MaxListed:N0} more assets, not listed.");
        }

        WriteKept(plan.Kept, "Kept (another source needs them, or their manifest cannot be trusted)", writer);
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
        WriteKept(result.ReuploadNeeded, "Re-upload needed (their blobs are gone but a manifest was written again)", writer);
        if (result.Plan.Matched.Count > 0)
        {
            writer.WriteLine("Occasions still count the deleted assets until you run: events rebuild, then events classify");
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
