using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Sepiabox.AppHost.Tests;

/// <summary>
/// The deploy order of S-032: <c>scripts/deploy.sh</c> runs against stand-ins for <c>azd</c> and <c>az</c>
/// that record their calls, so nothing reaches Azure.
/// </summary>
public sealed class DeployScriptTests : IDisposable
{
    private const string JobStart =
        $"az containerapp job start --name sepiabox-migrate --resource-group {ShutdownRunbook.ResourceGroup} --query name --output tsv";
    private const string JobStatus =
        $"az containerapp job execution show --name sepiabox-migrate --resource-group {ShutdownRunbook.ResourceGroup} --job-execution-name sepiabox-migrate-run1 --query properties.status --output tsv";
    private const string JobStop =
        $"az containerapp job stop --name sepiabox-migrate --resource-group {ShutdownRunbook.ResourceGroup} --job-execution-name sepiabox-migrate-run1";
    private const string DeployWeb = "azd deploy web --no-prompt";
    private const string DeployImport = "azd deploy import --no-prompt";
    private const string DeployImportAll = "azd deploy import-all --no-prompt";
    private const string DeployGeoNames = "azd deploy geonames --no-prompt";
    private const string PauseQueue =
        $"az servicebus queue update --resource-group {ShutdownRunbook.ResourceGroup} --namespace-name {ShutdownRunbook.ServiceBusNamespace} --name {ShutdownRunbook.Queue} --status ReceiveDisabled --output none";
    private const string ResumeQueue =
        $"az servicebus queue update --resource-group {ShutdownRunbook.ResourceGroup} --namespace-name {ShutdownRunbook.ServiceBusNamespace} --name {ShutdownRunbook.Queue} --status Active --output none";

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
              if [ "$2" = "$STUB_FAILING_DEPLOY" ]; then
                echo "deploy of $2 failed" >&2
                exit 1
              fi
            fi
            if [ "$1 $2" = "env get-value" ]; then
              case "$3" in
                AZURE_ENV_NAME) echo "$STUB_ENV_NAME" ;;
                AZURE_LOCATION) echo "$STUB_LOCATION" ;;
                AZURE_RESOURCE_GROUP)
                  if [ -z "$STUB_RESOURCE_GROUP" ]; then
                    echo "ERROR: key not found" >&2
                    exit 1
                  fi
                  echo "$STUB_RESOURCE_GROUP" ;;
              esac
            fi
            """);
        // Each status query answers the next word of STUB_STATUSES (the last one repeats); FAIL makes the
        // query itself fail, as a network error would.
        WriteStub("az", """
            echo "az $*" >> "$CALLS"
            case "$*" in
              "group exists"*)
                if [ "$STUB_PG_STATE" = "AZ_ERROR" ]; then
                  echo "ERROR: Please run 'az login' to setup account." >&2
                  exit 1
                fi
                if [ "$STUB_PG_STATE" = "NO_GROUP" ]; then echo "false"; else echo "true"; fi ;;
              "postgres flexible-server list"*)
                if [ "$STUB_PG_STATE" = "LIST_ERROR" ]; then
                  echo "ERROR: Please run 'az login' to setup account." >&2
                  exit 1
                fi
                if [ "$STUB_PG_STATE" != "MISSING" ]; then echo "$STUB_PG_STATE"; fi ;;
              "containerapp job start"*) echo "$STUB_EXECUTION" ;;
              "servicebus queue update"*"--status Active"*)
                if [ "$STUB_RESUME" = "FAIL" ]; then exit 1; fi ;;
              "containerapp job stop"*)
                if [ "$STUB_STOP" = "FAIL" ]; then exit 1; fi ;;
              "containerapp job execution show"*)
                n=$(( $(cat "$CALLS.polls" 2>/dev/null || echo 0) + 1 ))
                echo "$n" > "$CALLS.polls"
                status=$(echo "$STUB_STATUSES" | awk -v n="$n" '{ print (n <= NF ? $n : $NF) }')
                if [ "$status" = "FAIL" ]; then
                  echo "Connection reset" >&2
                  exit 1
                fi
                echo "$status" ;;
            esac
            """);
    }

    public void Dispose() => Directory.Delete(_stubs, recursive: true);

    [UnixFact]
    public async Task Deploy_migrates_the_cloud_database_before_the_web_when_the_job_succeeds()
    {
        var result = await RunDeployAsync("Succeeded");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                "azd provision --no-prompt",
                // Nothing receives from the queue until the import jobs run the new image (D-063).
                PauseQueue,
                "azd deploy migrate --no-prompt",
                JobStart,
                JobStatus,
                DeployImport,
                DeployImportAll,
                // The manual GeoNames job runs the new image too (S-059), before the queue receives again.
                DeployGeoNames,
                ResumeQueue,
                DeployWeb,
            ],
            Calls().Where(call => !call.StartsWith("azd env", StringComparison.Ordinal)
                && !call.StartsWith("az group", StringComparison.Ordinal)
                && !call.StartsWith("az postgres", StringComparison.Ordinal)));
        // What azure.yaml's predeploy hook checks, so that only this script deploys.
        Assert.Equal(["1", "1", "1", "1", "1"], File.ReadAllLines(_calls + ".guard"));
        Assert.Contains("Migrations applied.", result.Output, StringComparison.Ordinal);
    }

    [UnixTheory]
    [InlineData("Running Running Succeeded", 3)]
    [InlineData("Unknown Processing Succeeded", 3)]
    [InlineData("FAIL Running FAIL Succeeded", 4)]
    public async Task Deploy_keeps_polling_until_the_job_succeeds_when_it_is_running_or_a_query_fails(
        string statuses, int polls)
    {
        var result = await RunDeployAsync(statuses);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(polls, Calls().Count(call => call == JobStatus));
        Assert.Equal(DeployWeb, Calls()[^1]);
    }

    [UnixTheory]
    [InlineData("Failed")]
    [InlineData("Running Stopped")]
    [InlineData("Degraded")]
    public async Task Deploy_leaves_the_web_alone_when_the_migration_does_not_succeed(string statuses)
    {
        var result = await RunDeployAsync(statuses);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("azd deploy migrate --no-prompt", Calls());
        Assert.DoesNotContain(DeployWeb, Calls());
        Assert.DoesNotContain(DeployImport, Calls());
        Assert.DoesNotContain(DeployImportAll, Calls());
        Assert.DoesNotContain(DeployGeoNames, Calls());
        Assert.Contains(
            $"ended as {statuses.Split(' ')[^1]}; the web was not deployed.", result.Error, StringComparison.Ordinal);
        // The old import image still expects the old schema, which a failed migration leaves.
        Assert.Equal(ResumeQueue, Calls()[^1]);
    }

    [UnixTheory]
    [InlineData("Running", "", "The execution was stopped.")]
    [InlineData("FAIL", "", "The execution was stopped.")]
    [InlineData("Running", "FAIL", "Stopping the execution failed; stop it by hand: " + JobStop)]
    public async Task Deploy_stops_the_job_and_leaves_the_web_alone_when_the_migration_times_out(
        string statuses, string stop, string message)
    {
        var result = await RunDeployAsync(statuses, timeoutSeconds: 1, stop: stop);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(JobStop, Calls());
        Assert.DoesNotContain(DeployWeb, Calls());
        Assert.DoesNotContain(DeployImport, Calls());
        Assert.DoesNotContain(DeployImportAll, Calls());
        Assert.DoesNotContain(DeployGeoNames, Calls());
        Assert.Contains("did not finish in 1s", result.Error, StringComparison.Ordinal);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
        Assert.Contains("may be partly applied", result.Error, StringComparison.Ordinal);
        Assert.Equal(ResumeQueue, Calls()[^1]);
    }

    [UnixFact]
    public async Task Deploy_keeps_the_queue_paused_when_the_migration_succeeded_but_the_import_jobs_were_not_updated()
    {
        var result = await RunDeployAsync("Succeeded", failingDeploy: "import");

        Assert.NotEqual(0, result.ExitCode);
        // The old import image must not receive against the new schema.
        Assert.Equal([PauseQueue], Calls().Where(call => call.StartsWith("az servicebus", StringComparison.Ordinal)));
        Assert.Contains(
            "The import jobs were not updated, so the queue manifests is still paused. Run this script again, or resume it once the import jobs run the new image: az servicebus queue update --resource-group rg-sepiabox --namespace-name sepiabox-bus --name manifests --status Active",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(DeployWeb, Calls());
    }

    [UnixFact]
    public async Task Deploy_resumes_the_queue_but_leaves_the_web_alone_when_the_geonames_job_is_not_updated()
    {
        var result = await RunDeployAsync("Succeeded", failingDeploy: "geonames");

        Assert.NotEqual(0, result.ExitCode);
        // The import jobs already run the new image, so they may receive.
        Assert.Equal([PauseQueue, ResumeQueue], Calls().Where(call => call.StartsWith("az servicebus", StringComparison.Ordinal)));
        Assert.Equal(ResumeQueue, Calls()[^1]);
        Assert.DoesNotContain(DeployWeb, Calls());
    }

    [UnixFact]
    public async Task Deploy_resumes_the_queue_when_the_migration_fails()
    {
        // A failed migration leaves the old schema, which the old import image expects.
        var result = await RunDeployAsync("Failed");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal([PauseQueue, ResumeQueue], Calls().Where(call => call.StartsWith("az servicebus", StringComparison.Ordinal)));
    }

    [UnixFact]
    public async Task Deploy_fails_when_it_cannot_resume_the_queue_after_updating_the_import_jobs()
    {
        var result = await RunDeployAsync("Succeeded", resume: "FAIL");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Could not make the queue receive again", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(DeployWeb, Calls());
    }

    [UnixFact]
    public async Task Deploy_says_how_to_resume_the_queue_when_it_cannot()
    {
        var result = await RunDeployAsync("Failed", resume: "FAIL");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "Could not make the queue receive again; run: az servicebus queue update --resource-group rg-sepiabox --namespace-name sepiabox-bus --name manifests --status Active",
            result.Error,
            StringComparison.Ordinal);
    }

    [UnixFact]
    public async Task Deploy_never_touches_the_queue_when_it_refuses_to_start()
    {
        var result = await RunDeployAsync("Succeeded", postgresState: "Stopped");

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("az servicebus", StringComparison.Ordinal));
    }

    [UnixFact]
    public async Task Deploy_leaves_the_web_alone_when_the_job_start_returns_no_execution()
    {
        var result = await RunDeployAsync("Succeeded", execution: "");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("returned no execution name", result.Error, StringComparison.Ordinal);
        Assert.Equal(ResumeQueue, Calls()[^1]);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("az containerapp job execution", StringComparison.Ordinal));
        Assert.DoesNotContain(DeployWeb, Calls());
        Assert.DoesNotContain(DeployImport, Calls());
        Assert.DoesNotContain(DeployImportAll, Calls());
        Assert.DoesNotContain(DeployGeoNames, Calls());
    }

    [UnixFact]
    public async Task Deploy_succeeds_on_the_first_provision_when_azd_has_no_resource_group_yet()
    {
        var result = await RunDeployAsync("Succeeded", resourceGroup: "", postgresState: "NO_GROUP");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("az postgres", StringComparison.Ordinal));
        Assert.Equal(DeployWeb, Calls()[^1]);
    }

    [UnixTheory]
    [InlineData("sepiabox", "westeurope", "", "must be in francecentral")]
    [InlineData("other", "francecentral", "", "Select the azd environment 'sepiabox'")]
    [InlineData("sepiabox", "francecentral", "rg-other", "deploys to rg-other, not rg-sepiabox")]
    public async Task Deploy_refuses_to_start_when_the_azd_environment_is_not_sepiabox_in_france_central(
        string environmentName, string location, string resourceGroup, string message)
    {
        var result = await RunDeployAsync("Succeeded", environmentName, location, resourceGroup);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
        Assert.All(Calls(), call => Assert.StartsWith("azd env get-value", call, StringComparison.Ordinal));
    }

    [UnixTheory]
    [InlineData("Stopped")]
    [InlineData("Stopping")]
    [InlineData("Starting")]
    public async Task Deploy_refuses_to_start_when_postgres_is_not_ready(string state)
    {
        var result = await RunDeployAsync("Succeeded", postgresState: state);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"sepiabox-pg is {state}", result.Error, StringComparison.Ordinal);
        Assert.Contains("resume.sh", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("azd provision --no-prompt", Calls());
    }

    [UnixTheory]
    [InlineData("AZ_ERROR")]
    [InlineData("LIST_ERROR")]
    public async Task Deploy_stops_when_the_postgres_check_fails(string failure)
    {
        // A failed login or network error, whether on the group or the server query, must not skip the
        // check and deploy past a stopped server.
        var result = await RunDeployAsync("Succeeded", postgresState: failure);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("az login", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("azd provision --no-prompt", Calls());
    }

    [UnixFact]
    public async Task Deploy_provisions_the_server_when_it_does_not_exist_yet()
    {
        var result = await RunDeployAsync("Succeeded", postgresState: "MISSING");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("azd provision --no-prompt", Calls());
    }

    [UnixTheory]
    [InlineData("", 1)]
    [InlineData("1", 0)]
    public async Task Azd_predeploy_hook_refuses_a_deploy_that_does_not_come_from_the_script(string guard, int expected)
    {
        var azureYaml = await File.ReadAllTextAsync(Path.Combine(Repository.Root, "azure.yaml"));
        var hook = Regex.Match(azureYaml, @"predeploy:\s+posix:\s+shell: sh\s+run: '(?<run>[^']+)'");
        Assert.True(hook.Success, "azure.yaml has no POSIX predeploy hook.");

        var result = await RunAsync("sh", ["-c", hook.Groups["run"].Value], new() { ["SEPIABOX_DEPLOY_SCRIPT"] = guard });

        Assert.Equal(expected, result.ExitCode);
    }

    private Task<ProcessResult> RunDeployAsync(
        string statuses,
        string environmentName = "sepiabox",
        string location = "francecentral",
        string resourceGroup = "rg-sepiabox",
        string postgresState = "Ready",
        int timeoutSeconds = 30,
        string execution = "sepiabox-migrate-run1",
        string stop = "",
        string failingDeploy = "",
        string resume = "") =>
        RunAsync("bash", [Path.Combine(Repository.Root, "scripts", "deploy.sh")], new()
        {
            ["PATH"] = _stubs + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["CALLS"] = _calls,
            ["STUB_STATUSES"] = statuses,
            ["STUB_ENV_NAME"] = environmentName,
            ["STUB_LOCATION"] = location,
            ["STUB_RESOURCE_GROUP"] = resourceGroup,
            ["STUB_PG_STATE"] = postgresState,
            ["STUB_EXECUTION"] = execution,
            ["STUB_STOP"] = stop,
            ["STUB_FAILING_DEPLOY"] = failingDeploy,
            ["STUB_RESUME"] = resume,
            ["SEPIABOX_MIGRATE_TIMEOUT_SECONDS"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SEPIABOX_MIGRATE_POLL_SECONDS"] = "0.1",
        });

    private static async Task<ProcessResult> RunAsync(string fileName, string[] arguments, Dictionary<string, string> environment)
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

        return new ProcessResult(process.ExitCode, await output, await error);
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

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
