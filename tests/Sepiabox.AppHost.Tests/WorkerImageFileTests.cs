namespace Sepiabox.AppHost.Tests;

/// <summary>
/// The worker's Dockerfile (S-036, D-107), read as text: CI's worker-image job builds it and runs it. This keeps the
/// parts that must not change without that job noticing.
/// </summary>
public sealed class WorkerImageFileTests
{
    private static readonly string s_dockerfile = File.ReadAllText(Path.Combine(Repository.Root, "infra", "worker", "Dockerfile"));

    [Fact]
    public void Installs_ffmpeg_in_the_runtime_stage_and_runs_the_worker_as_the_app_user()
    {
        // The plain runtime image fails at start: ServiceDefaults references Microsoft.AspNetCore.App.
        Assert.DoesNotContain("FROM mcr.microsoft.com/dotnet/runtime", s_dockerfile, StringComparison.Ordinal);
        var runtime = s_dockerfile[s_dockerfile.LastIndexOf("FROM mcr.microsoft.com/dotnet/aspnet:10.0", StringComparison.Ordinal)..];

        Assert.Contains("apt-get install --yes --no-install-recommends ffmpeg", runtime, StringComparison.Ordinal);
        Assert.Contains("USER app", runtime, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"/app/sepiabox-worker.dll\"]", runtime, StringComparison.Ordinal);
        Assert.Contains("ENV Search__ModelFolder=/models", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Downloads_the_models_it_needs_with_the_verified_downloader_at_build_time()
    {
        Assert.Contains("models download --models /models", s_dockerfile, StringComparison.Ordinal);
        foreach (var model in new[] { "clip-vit-b32-vision", "yunet-2023mar", "sface-2021dec" })
        {
            Assert.Contains(model, s_dockerfile, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Is_built_in_ci_and_its_context_leaves_out_what_does_not_belong_in_it()
    {
        var workflow = File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "ci.yml"));
        var ignore = File.ReadAllText(Path.Combine(Repository.Root, ".dockerignore"));

        Assert.Contains("docker build --file infra/worker/Dockerfile", workflow, StringComparison.Ordinal);
        Assert.Contains("entrypoint ffmpeg", workflow, StringComparison.Ordinal);
        Assert.Contains("**/bin/", ignore, StringComparison.Ordinal);
        Assert.Contains(".git/", ignore, StringComparison.Ordinal);
        // Sepiabox.Search and Sepiabox.Pipeline embed docs/schemas/*.json, so the context must keep that folder.
        Assert.Contains("!docs/schemas/", ignore, StringComparison.Ordinal);
    }
}
