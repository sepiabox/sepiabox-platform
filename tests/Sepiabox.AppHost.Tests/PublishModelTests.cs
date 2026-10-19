namespace Sepiabox.AppHost.Tests;

// Builds the publish model only; nothing is started, so it needs no containers.
public sealed class PublishModelTests : IDisposable
{
    private readonly string _output = Path.Combine(Path.GetTempPath(), "sepiabox-publish-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_output))
        {
            Directory.Delete(_output, recursive: true);
        }
    }

    [Fact]
    public async Task Publishing_does_not_include_the_run_once_workers()
    {
        // Published as projects they would become always-on Container Apps; S-033 adds the real job.
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Sepiabox_AppHost>(
            ["--operation", "publish", "--publisher", "manifest", "--output-path", _output]);

        var names = builder.Resources.Select(resource => resource.Name).ToList();

        Assert.Contains("web", names);
        Assert.DoesNotContain(names, name => name.StartsWith("worker", StringComparison.Ordinal));
    }
}
