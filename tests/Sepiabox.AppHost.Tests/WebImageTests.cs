using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Sepiabox.AppHost.Tests;

/// <summary>
/// The web's image as a deploy builds it (S-056, D-071), against a local registry instead of Azure's: the models image
/// from scripts/web-models.sh with the real, verified model, then the web published twice on it as two releases.
/// Needs Docker and the CLIP text model, which CI's integration job has.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WebImageTests
{
    private const string WebRepository = "sepiabox-web-test";

    // Pinned: the test should not change because the registry image did.
    private const string Registry = "registry:2.8.3@sha256:a3d8aaa63ed8681a604f1dea0aa03f100d5895b6a58ace528858a7b332415373";

    [WebImageFact]
    public async Task Two_releases_share_the_model_layer_and_carry_the_pinned_model_readable_by_the_app()
    {
        var container = (await Run("docker", ["run", "--detach", "--publish", "127.0.0.1::5000", Registry])).Trim();
        var images = new List<string>();
        var output = Path.Combine(Path.GetTempPath(), "sepiabox-web-image-" + Guid.NewGuid().ToString("N"));

        // From a copy of the repository, so the Release restore and build for linux-x64 (the ingest's in
        // web-models.sh, the web's here) never rewrite the obj folders the other test projects are using.
        var copy = Path.Combine(output, "repository");
        try
        {
            await CopyRepositoryAsync(copy);
            var port = (await Run("docker", ["port", container, "5000/tcp"])).Split('\n')[0].Split(':')[^1].Trim();
            var registry = $"localhost:{port}";

            var modelsImage = (await Run("bash", [Path.Combine(copy, "scripts", "web-models.sh")], new() { ["SEPIABOX_REGISTRY"] = registry })).Trim();
            images.Add(modelsImage);
            foreach (var release in new[] { "one", "two" })
            {
                // Two app commits: the same models image, a different app.
                await Run(
                    "dotnet",
                    [
                        "publish", Path.Combine(copy, "src", "Sepiabox.Web", "Sepiabox.Web.csproj"), "--configuration", "Release",
                        "--runtime", "linux-x64", "--self-contained", "false", "-t:PublishContainer",
                        $"-p:ContainerRegistry={registry}", $"-p:ContainerRepository={WebRepository}", $"-p:ContainerImageTag={release}",
                        $"-p:InformationalVersion=1.0.0+{release}",
                        "--output", Path.Combine(output, release),
                    ],
                    new() { ["SEPIABOX_WEB_BASE_IMAGE"] = modelsImage });
                images.Add($"{registry}/{WebRepository}:{release}");
            }

            using var http = new HttpClient();
            var baseLayers = await LayersAsync(http, registry, "sepiabox-web-models", modelsImage.Split(':')[^1]);
            var one = await LayersAsync(http, registry, WebRepository, "one");
            var two = await LayersAsync(http, registry, WebRepository, "two");

            // Each release is the models image plus its app: the model layer, the models image's last, is the same blob.
            Assert.Equal(baseLayers, one.Take(baseLayers.Count));
            Assert.Equal(baseLayers, two.Take(baseLayers.Count));
            Assert.NotEqual(one, two);

            // The pinned SHA-256 of models.json, read inside the published web as its non-root user.
            using var models = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Repository.Root, "models", "models.json")));
            foreach (var model in models.RootElement.GetProperty("models").EnumerateArray()
                .Where(m => m.GetProperty("name").GetString() is "clip-vit-b32-text" or "clip-bpe-vocab"))
            {
                var sum = await Run("docker", ["run", "--rm", "--user", "app", "--entrypoint", "sha256sum", $"{registry}/{WebRepository}:one", "/models/" + model.GetProperty("file").GetString()]);
                Assert.StartsWith(model.GetProperty("sha256").GetString()!, sum, StringComparison.Ordinal);
            }
        }
        finally
        {
            await Run("docker", ["rm", "--force", container], check: false);
            foreach (var image in images)
            {
                await Run("docker", ["rmi", "--force", image], check: false);
            }

            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    /// <summary>The tracked and untracked, not ignored, files: the sources as they are, without bin and obj.</summary>
    private static async Task CopyRepositoryAsync(string destination)
    {
        var files = await Run("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard"]);
        foreach (var file in files.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var source = Path.Combine(Repository.Root, file);
            if (!File.Exists(source))
            {
                // Deleted in the working tree but still in the index.
                continue;
            }

            var target = Path.Combine(destination, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
    }

    private static async Task<List<string>> LayersAsync(HttpClient http, string registry, string repository, string tag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://{registry}/v2/{repository}/manifests/{tag}"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. manifest.RootElement.GetProperty("layers").EnumerateArray().Select(l => l.GetProperty("digest").GetString()!)];
    }

    private static async Task<string> Run(string fileName, string[] arguments, Dictionary<string, string>? environment = null, bool check = true)
    {
        var start = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Repository.Root };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? [])
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(!check || process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed: {await error}");
        return await output;
    }
}

/// <summary>
/// Needs Docker (Linux) and the CLIP text model in the models folder: skipped without them, except that CI, which sets
/// SEPIABOX_REQUIRE_MODELS, never skips it for a missing model.
/// </summary>
internal sealed class WebImageFactAttribute : FactAttribute
{
    public WebImageFactAttribute()
    {
        var folder = Environment.GetEnvironmentVariable("SEPIABOX_MODELS") is { Length: > 0 } models
            ? models
            : OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sepiabox", "models")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sepiabox", "models");
        if (OperatingSystem.IsWindows())
        {
            Skip = "Needs Docker with Linux images, as the Linux integration job has.";
        }
        else if (!File.Exists(Path.Combine(folder, "clip-vit-b32-text.onnx")) && Environment.GetEnvironmentVariable("SEPIABOX_REQUIRE_MODELS") != "1")
        {
            Skip = $"The CLIP text model is not in {folder}; run `sepiabox-ingest models download`.";
        }
    }
}
