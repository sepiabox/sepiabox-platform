namespace Sepiabox.AppHost.Tests;

// Builds the local (run mode) model only; nothing is started, so it needs no containers.
public sealed class RunModelTests
{
    [Fact]
    public async Task Local_run_has_the_run_once_workers_and_no_cloud_only_resource()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Sepiabox_AppHost>();

        var names = builder.Resources.Select(resource => resource.Name).ToList();

        Assert.Contains("worker-migrate", names);
        Assert.Contains("worker-import", names);
        // These have no emulator: in run mode Aspire would provision them in an Azure subscription.
        Assert.DoesNotContain("vault", names);
        Assert.DoesNotContain("insights", names);
        Assert.DoesNotContain("registry", names);
        Assert.DoesNotContain("aca", names);
        Assert.DoesNotContain("migrate", names);
        Assert.DoesNotContain("geonames", names);
    }
}
