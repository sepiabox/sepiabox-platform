namespace Sepiabox.Worker.Assets;

/// <summary>
/// <c>assets delete</c> end to end (S-057): the dry run, the <c>--expect</c> check that refuses to start, the deletion
/// and the exit code (0 done, 1 some assets kept or failed, 2 usage), with what it prints.
/// </summary>
public static class AssetsDeleteCommand
{
    public static async Task<int> RunAsync(
        AssetDeletion deletion, string source, bool yes, int? expect, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        var plan = await deletion.PlanAsync(source, cancellationToken);
        AssetDeletionReport.WritePlan(plan, dryRun: !yes, output);
        if (!yes)
        {
            return ExitCodes.Success;
        }

        // Nothing is deleted unless the person saw this number: a wrong source name or a changed library stops here.
        if (plan.Matched.Count != expect)
        {
            await error.WriteLineAsync($"{plan.Matched.Count:N0} assets match, not {expect:N0}: nothing was deleted. Run it without --yes to see them.");
            return ExitCodes.Usage;
        }

        var result = await deletion.DeleteAsync(plan, cancellationToken);
        AssetDeletionReport.WriteResult(result, output);
        return result.Clean ? ExitCodes.Success : ExitCodes.Failed;
    }
}
