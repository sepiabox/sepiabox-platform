namespace Sepiabox.AppHost.Tests;

[Trait("Category", "Integration")]
public sealed class PublishModelTests
{
    [Fact]
    public async Task Publishing_does_not_include_the_run_once_workers()
    {
        // Published as projects they would become always-on Container Apps; S-033 adds the real job.
        var output = Path.Combine(Path.GetTempPath(), "sepiabox-publish-" + Guid.NewGuid().ToString("N"));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Sepiabox_AppHost>(
            ["--operation", "publish", "--publisher", "manifest", "--output-path", output]);

        var names = builder.Resources.Select(resource => resource.Name).ToList();

        Assert.Contains("web", names);
        Assert.DoesNotContain(names, name => name.StartsWith("worker", StringComparison.Ordinal));
    }
}
