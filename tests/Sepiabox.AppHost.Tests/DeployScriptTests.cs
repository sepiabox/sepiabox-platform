using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Sepiabox.AppHost.Tests;

/// <summary>
/// The deploy order of S-032: <c>scripts/deploy.sh</c> runs against stand-ins for <c>azd</c> and <c>az</c>
/// that record their calls, so nothing reaches Azure.
/// </summary>
public sealed class DeployScriptTests : IDisposable
{
    private static readonly string s_root = Repository.Root;
    private readonly string _stubs = Path.Combine(Path.GetTempPath(), "sepiabox-deploy-" + Guid.NewGuid().ToString("N"));
    private readonly string _calls;

    public DeployScriptTests()
    {
        Directory.CreateDirectory(_stubs);
        _calls = Path.Combine(_stubs, "calls.txt");
        WriteStub("azd", """
            echo "azd $*" >> "$CALLS"
            if [ "$1" = "deploy" ]; then
              echo "$SEPIABOX_DEPLOY_SCRIPT" >> "$CALLS.guard"
            fi
            if [ "$1 $2" = "env get-value" ]; then
              case "$3" in
                AZURE_ENV_NAME) echo "$STUB_ENV_NAME" ;;
                AZURE_LOCATION) echo "$STUB_LOCATION" ;;
              esac
            fi
            """);
        WriteStub("az", """
            echo "az $*" >> "$CALLS"
            case "$*" in
              "containerapp job start"*) echo "sepiabox-migrate-run1" ;;
              "containerapp job execution show"*) echo "$STUB_STATUS" ;;
            esac
            """);
    }

    public void Dispose() => Directory.Delete(_stubs, recursive: true);

    [UnixFact]
    public async Task Deploy_migrates_the_cloud_database_before_the_web_when_the_job_succeeds()
    {
        var exitCode = await RunDeployAsync(status: "Succeeded");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "azd provision --no-prompt",
                "azd deploy migrate --no-prompt",
                $"az containerapp job start --name sepiabox-migrate --resource-group {ShutdownRunbook.ResourceGroup} --query name --output tsv",
                $"az containerapp job execution show --name sepiabox-migrate --resource-group {ShutdownRunbook.ResourceGroup} --job-execution-name sepiabox-migrate-run1 --query properties.status --output tsv",
                "azd deploy web --no-prompt",
            ],
            Calls().Where(call => !call.StartsWith("azd env", StringComparison.Ordinal)));
        // What azure.yaml's predeploy hook checks, so that only this script deploys.
        Assert.Equal(["1", "1"], File.ReadAllLines(_calls + ".guard"));
    }

    [UnixTheory]
    [InlineData("Failed")]
    [InlineData("Stopped")]
    [InlineData("Degraded")]
    public async Task Deploy_leaves_the_web_alone_when_the_migration_does_not_succeed(string status)
    {
        var exitCode = await RunDeployAsync(status);

        Assert.Equal(1, exitCode);
        Assert.Contains("azd deploy migrate --no-prompt", Calls());
        Assert.DoesNotContain(Calls(), call => call.StartsWith("azd deploy web", StringComparison.Ordinal));
    }

    [UnixTheory]
    [InlineData("sepiabox", "westeurope")]
    [InlineData("other", "francecentral")]
    public async Task Deploy_refuses_to_start_when_the_azd_environment_is_not_sepiabox_in_france_central(
        string environmentName, string location)
    {
        var exitCode = await RunDeployAsync("Succeeded", environmentName, location);

        Assert.Equal(1, exitCode);
        Assert.All(Calls(), call => Assert.StartsWith("azd env get-value", call, StringComparison.Ordinal));
    }

    [UnixTheory]
    [InlineData("", 1)]
    [InlineData("1", 0)]
    public async Task Azd_predeploy_hook_refuses_a_deploy_that_does_not_come_from_the_script(string guard, int expected)
    {
        var azureYaml = await File.ReadAllTextAsync(Path.Combine(s_root, "azure.yaml"));
        var hook = Regex.Match(azureYaml, @"predeploy:\s+posix:\s+shell: sh\s+run: '(?<run>[^']+)'");
        Assert.True(hook.Success, "azure.yaml has no POSIX predeploy hook.");

        var exitCode = await RunAsync("sh", ["-c", hook.Groups["run"].Value], new() { ["SEPIABOX_DEPLOY_SCRIPT"] = guard });

        Assert.Equal(expected, exitCode);
    }

    private Task<int> RunDeployAsync(string status, string environmentName = "sepiabox", string location = "francecentral") =>
        RunAsync("bash", [Path.Combine(s_root, "scripts", "deploy.sh")], new()
        {
            ["PATH"] = _stubs + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["CALLS"] = _calls,
            ["STUB_STATUS"] = status,
            ["STUB_ENV_NAME"] = environmentName,
            ["STUB_LOCATION"] = location,
        });

    private static async Task<int> RunAsync(string fileName, string[] arguments, Dictionary<string, string> environment)
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await Task.WhenAll(output, error);
        return process.ExitCode;
    }

    private List<string> Calls() => File.Exists(_calls) ? [.. File.ReadAllLines(_calls)] : [];

    private void WriteStub(string name, string body)
    {
        var path = Path.Combine(_stubs, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body.ReplaceLineEndings("\n") + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
