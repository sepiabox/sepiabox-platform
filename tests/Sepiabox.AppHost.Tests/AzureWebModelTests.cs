using System.Diagnostics;
using System.Security.Cryptography;

namespace Sepiabox.AppHost.Tests;

/// <summary>The CLIP text model in the web's image and the web's size in Azure (S-056, D-071).</summary>
public sealed class AzureWebModelTests(PublishedInfrastructure published) : IClassFixture<PublishedInfrastructure>
{
    [Fact]
    public void Web_has_1_gib_of_memory_and_scales_to_zero_when_published()
    {
        var web = published.The("Microsoft.App/containerApps");

        Assert.Contains("memory: '1Gi'", web.Block("resources"), StringComparison.Ordinal);
        Assert.Contains("cpu: json('0.5')", web.Block("resources"), StringComparison.Ordinal);
        Assert.Contains("minReplicas: 0", web.Block("scale"), StringComparison.Ordinal);
        Assert.Contains("maxReplicas: 1", web.Block("scale"), StringComparison.Ordinal);
    }

    [Fact]
    public void Web_reads_the_model_from_the_folder_its_base_image_carries_when_published()
    {
        var environment = published.Resource("web")["env"]!.AsObject();

        Assert.Equal("/models", environment["Search__ModelFolder"]!.GetValue<string>());
        Assert.Contains("COPY --chmod=a+rX models/ /models/", File.ReadAllText(Path.Combine(Repository.Root, "infra", "web-models", "Dockerfile")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "mcr.microsoft.com/dotnet/aspnet:10.0")]
    [InlineData("example.azurecr.io/sepiabox-web-models:0123456789abcdef", "example.azurecr.io/sepiabox-web-models:0123456789abcdef")]
    public async Task Web_is_built_on_the_models_image_only_when_the_deploy_names_one(string named, string expected)
    {
        // What azd's publish of the web sees: scripts/deploy.sh exports SEPIABOX_WEB_BASE_IMAGE.
        var result = await RunAsync(
            "dotnet",
            ["msbuild", Path.Combine(Repository.Root, "src", "Sepiabox.Web", "Sepiabox.Web.csproj"), "-getProperty:ContainerBaseImage"],
            new() { ["SEPIABOX_WEB_BASE_IMAGE"] = named });

        Assert.Equal(expected, result.Trim());
    }

    [Fact]
    public void Web_models_script_copies_the_files_models_json_pins()
    {
        // scripts/web-models.sh names the files; models.json is where they are pinned (D-070).
        var script = File.ReadAllText(Path.Combine(Repository.Root, "scripts", "web-models.sh"));
        var models = File.ReadAllText(Path.Combine(Repository.Root, "models", "models.json"));

        foreach (var (name, file) in new[] { ("clip-vit-b32-text", "clip-vit-b32-text.onnx"), ("clip-bpe-vocab", "clip-bpe-simple-vocab-16e6.txt.gz") })
        {
            Assert.Contains($"\"name\": \"{name}\"", models, StringComparison.Ordinal);
            Assert.Contains($"\"file\": \"{file}\"", models, StringComparison.Ordinal);
            Assert.Contains(file, script, StringComparison.Ordinal);
            Assert.Contains(name, script, StringComparison.Ordinal);
        }
    }

    private static async Task<string> RunAsync(string fileName, string[] arguments, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}

/// <summary>The web's models image, built with Docker from a context like the one scripts/web-models.sh prepares.</summary>
[Trait("Category", "Integration")]
public sealed class WebModelsImageTests
{
    [UnixFact("Needs Docker, as the integration job has.")]
    public async Task The_models_image_carries_each_file_byte_for_byte_in_models()
    {
        var context = Path.Combine(Path.GetTempPath(), "sepiabox-web-models-" + Guid.NewGuid().ToString("N"));
        var image = "sepiabox-web-models-test:" + Guid.NewGuid().ToString("N")[..12];
        Directory.CreateDirectory(Path.Combine(context, "models"));
        try
        {
            // Stand-ins for the model and tokenizer: the image must keep whatever models download verified.
            var files = new Dictionary<string, byte[]>
            {
                ["clip-vit-b32-text.onnx"] = RandomNumberGenerator.GetBytes(4096),
                ["clip-bpe-simple-vocab-16e6.txt.gz"] = RandomNumberGenerator.GetBytes(1024),
                ["models.json"] = File.ReadAllBytes(Path.Combine(Repository.Root, "models", "models.json")),
            };
            foreach (var (name, bytes) in files)
            {
                await File.WriteAllBytesAsync(Path.Combine(context, "models", name), bytes);
            }

            // As a download can leave it: readable by its owner only. The image must still let the app read it.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path.Combine(context, "models", "clip-vit-b32-text.onnx"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            await Docker(
                "build", "--provenance=false", "--sbom=false",
                "--file", Path.Combine(Repository.Root, "infra", "web-models", "Dockerfile"), "--tag", image, context);
            foreach (var (name, bytes) in files)
            {
                // As the web runs: the image's non-root user.
                var sum = await Docker("run", "--rm", "--user", "app", "--entrypoint", "sha256sum", image, "/models/" + name);
                Assert.StartsWith(Convert.ToHexStringLower(SHA256.HashData(bytes)), sum, StringComparison.Ordinal);
            }

            // The app's base: the same ASP.NET Core runtime the web's own image names.
            Assert.Contains("Microsoft.AspNetCore.App 10.", await Docker("run", "--rm", "--entrypoint", "dotnet", image, "--list-runtimes"), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(context, recursive: true);
            await Docker(check: false, "rmi", "--force", image);
        }
    }

    private static Task<string> Docker(params string[] arguments) => Docker(check: true, arguments);

    private static async Task<string> Docker(bool check, params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(!check || process.ExitCode == 0, $"docker {string.Join(' ', arguments)}: {await error}");
        return await output;
    }
}
