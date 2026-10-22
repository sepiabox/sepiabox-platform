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
    private const string DeployFaces = "azd deploy faces --no-prompt";
    private const string DeployProcess = "azd deploy process --no-prompt";
    private const string DeploySync = "azd deploy sync --no-prompt";
    private const string PauseQueue =
        $"az servicebus queue update --resource-group {ShutdownRunbook.ResourceGroup} --namespace-name {ShutdownRunbook.ServiceBusNamespace} --name {ShutdownRunbook.Queue} --status ReceiveDisabled --output none";
    private const string ResumeQueue =
        $"az servicebus queue update --resource-group {ShutdownRunbook.ResourceGroup} --namespace-name {ShutdownRunbook.ServiceBusNamespace} --name {ShutdownRunbook.Queue} --status Active --output none";

    private readonly string _stubs = Path.Combine(Path.GetTempPath(), "sepiabox-deploy-" + Guid.NewGuid().ToString("N"));
    private readonly string _calls;

    private string Models => Path.Combine(_stubs, "models");

    public DeployScriptTests()
    {
        Directory.CreateDirectory(_stubs);
        _calls = Path.Combine(_stubs, "calls.txt");
        WriteStub("azd", """
            echo "azd $*" >> "$CALLS"
            if [ "$1" = "deploy" ]; then
              echo "$SEPIABOX_DEPLOY_SCRIPT" >> "$CALLS.guard"
              echo "$2 ${SEPIABOX_WEB_BASE_IMAGE:-}" >> "$CALLS.base"
              if [ "$2" = "$STUB_FAILING_DEPLOY" ]; then
                echo "deploy of $2 failed" >&2
                exit 1
              fi
            fi
            if [ "$1 $2 $3" = "env config set" ]; then
              echo "${4#infra.parameters.}=$5" >> "$CALLS.config"
            fi
            if [ "$1 $2 $3" = "env config get" ]; then
              # STUB_CONFIG holds the infra.parameters the environment already has, as name=value words, and
              # the script's own sets follow; azd prints a value as JSON.
              for entry in $STUB_CONFIG $(cat "$CALLS.config" 2>/dev/null); do
                if [ "${entry%%=*}" = "${4#infra.parameters.}" ]; then
                  echo "\"${entry#*=}\""
                  exit 0
                fi
              done
              echo "ERROR: no value at path '$4'" >&2
              exit 1
            fi
            if [ "$1 $2" = "env get-value" ]; then
              case "$3" in
                AZURE_CUSTOM_DOMAIN*|AZURE_UPLOADER_PRINCIPAL_IDS)
                  # STUB_DOTENV holds the azd environment's other values, as name=value words.
                  for entry in $STUB_DOTENV; do
                    if [ "${entry%%=*}" = "$3" ]; then
                      echo "${entry#*=}"
                      exit 0
                    fi
                  done
                  echo "ERROR: key '$3' not found in the environment values" >&2
                  exit 1 ;;
                AZURE_ENV_NAME) echo "$STUB_ENV_NAME" ;;
                AZURE_LOCATION) echo "$STUB_LOCATION" ;;
                AZURE_CONTAINER_REGISTRY_ENDPOINT) echo "sepiaboxacr.azurecr.io" ;;
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
              "containerapp hostname list"*)
                if [ "$STUB_HOSTNAMES" = "FAIL" ]; then exit 1; fi
                for host in $STUB_HOSTNAMES; do echo "$host"; done ;;
              "acr repository show"*)
                if [ "$STUB_MODELS_IMAGE" = "MISSING" ]; then exit 1; fi ;;
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
        // Docker and the .NET SDK, for scripts/web-models.sh (S-056): the build records its context's files.
        WriteStub("docker", """
            echo "docker $*" >> "$CALLS"
            case "$1 $2" in
              "image inspect"*) echo "mcr.microsoft.com/dotnet/aspnet@sha256:0123abcd" ;;
              build*)
                for last; do :; done
                ls "$last/models" > "$CALLS.context" ;;
            esac
            """);
        WriteStub("dotnet", """
            echo "dotnet $*" >> "$CALLS"
            if [ "$STUB_DOWNLOAD" = "FAIL" ]; then
              echo "clip-vit-b32-text: SHA-256 differs" >&2
              exit 1
            fi
            """);
        Directory.CreateDirectory(Models);
        File.WriteAllText(Path.Combine(Models, "clip-vit-b32-text.onnx"), "model");
        File.WriteAllText(Path.Combine(Models, "clip-bpe-simple-vocab-16e6.txt.gz"), "merges");
        File.WriteAllText(Path.Combine(Models, "clip-vit-b32-vision.onnx"), "vision");
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
                // The web's base image with the CLIP text model (S-056), already in the registry here: before the
                // queue is paused and the schema changes, so a failure there leaves everything as it was.
                "az acr login --name sepiaboxacr",
                "docker pull --quiet mcr.microsoft.com/dotnet/aspnet:10.0",
                "docker image inspect --format {{index .RepoDigests 0}} mcr.microsoft.com/dotnet/aspnet:10.0",
                $"az acr repository show --name sepiaboxacr --image sepiabox-web-models:{ModelsTag()} --output none",
                // Nothing receives from the queue until the import jobs run the new image (D-063).
                PauseQueue,
                "azd deploy migrate --no-prompt",
                JobStart,
                JobStatus,
                DeployImport,
                DeployImportAll,
                // The scheduled faces job (S-022) never runs an image older than the schema either.
                DeployFaces,
                // The event-driven process job (S-036), from its own Dockerfile.
                DeployProcess,
                // The scheduled Drive sync (S-038) after the job that processes what it stages.
                DeploySync,
                // The manual GeoNames job runs the new image too (S-059), before the queue receives again.
                DeployGeoNames,
                ResumeQueue,
                DeployWeb,
            ],
            Calls().Where(call => !call.StartsWith("azd env", StringComparison.Ordinal)
                && !call.StartsWith("az group", StringComparison.Ordinal)
                && !call.StartsWith("az postgres", StringComparison.Ordinal)));
        // What azure.yaml's predeploy hook checks, so that only this script deploys.
        Assert.Equal(["1", "1", "1", "1", "1", "1", "1", "1"], File.ReadAllLines(_calls + ".guard"));
        Assert.Equal($"web sepiaboxacr.azurecr.io/sepiabox-web-models:{ModelsTag()}", File.ReadAllLines(_calls + ".base")[^1]);
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
    public async Task Deploy_refuses_to_start_when_sepiabox_registry_is_set()
    {
        var result = await RunDeployAsync("Succeeded", registry: "localhost:5000");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("SEPIABOX_REGISTRY", result.Error, StringComparison.Ordinal);
        Assert.Empty(Calls());
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
    public async Task Deploy_gives_unset_azd_parameters_their_none_values_before_provisioning()
    {
        var result = await RunDeployAsync("Succeeded");

        Assert.Equal(0, result.ExitCode);
        // azd reads them only from infra.parameters (D-072); unset, --no-prompt would stop the deploy.
        var calls = Calls().Select(call => call.TrimEnd()).ToList();
        var provision = calls.IndexOf("azd provision --no-prompt");
        foreach (var set in new[]
        {
            "azd env config set infra.parameters.customDomain",
            "azd env config set infra.parameters.customDomainBinding Disabled",
            "azd env config set infra.parameters.uploaderPrincipalIds",
        })
        {
            Assert.InRange(calls.IndexOf(set), 0, provision);
        }
    }

    [UnixFact]
    public async Task Deploy_keeps_the_azd_parameters_the_owner_set()
    {
        var result = await RunDeployAsync("Succeeded", config: "customDomain= customDomainBinding=Disabled uploaderPrincipalIds=");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("azd env config set", StringComparison.Ordinal));
    }

    [UnixTheory]
    [InlineData("AZURE_CUSTOM_DOMAIN=app.example.com", null)]
    [InlineData("AZURE_UPLOADER_PRINCIPAL_IDS=00000000-0000-0000-0000-00000000000a", null)]
    [InlineData("", "AZURE_CUSTOM_DOMAIN_BINDING")]
    [InlineData("", "AZURE_CUSTOM_DOMAIN")]
    public async Task Deploy_refuses_an_azure_value_that_would_override_the_azd_parameters(string dotenv, string? shellVariable)
    {
        // Even empty in the shell: azd's deploy would use it instead of infra.parameters.
        var result = await RunDeployAsync(
            "Succeeded", dotenv: dotenv, extra: shellVariable is null ? null : new() { [shellVariable] = "" });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("delete its line from .azure/<environment>/.env", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("azd provision", StringComparison.Ordinal)
            || call.StartsWith("azd env config set", StringComparison.Ordinal)
            || call.StartsWith("az servicebus", StringComparison.Ordinal));
    }

    [UnixTheory]
    [InlineData("", "SniEnabled")]
    [InlineData("sepiabox-web.example.com", "SniEnabled")]
    [InlineData("FAIL", "SniEnabled")]
    // Set by hand locally in another case; the workflow normalizes it, deploy.sh must not rely on that.
    [InlineData("", "snienabled")]
    public async Task Deploy_refuses_to_bind_a_custom_domain_the_web_does_not_have_yet(string hostnames, string binding)
    {
        var result = await RunDeployAsync(
            "Succeeded", config: $"customDomain=app.example.com customDomainBinding={binding} uploaderPrincipalIds=", hostnames: hostnames);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("set SniEnabled and deploy again", result.Error, StringComparison.Ordinal);
        Assert.Contains("az containerapp hostname list --name sepiabox-web --resource-group rg-sepiabox --query [].name --output tsv", Calls());
        Assert.DoesNotContain(Calls(), call => call.StartsWith("azd provision", StringComparison.Ordinal)
            || call.StartsWith("az servicebus", StringComparison.Ordinal));
    }

    [UnixTheory]
    [InlineData("customDomain=app.example.com customDomainBinding=SniEnabled uploaderPrincipalIds=", "sepiabox-web.example.com App.Example.com")]
    [InlineData("customDomain=app.example.com customDomainBinding=Disabled uploaderPrincipalIds=", "")]
    public async Task Deploy_provisions_a_custom_domain_once_its_host_name_is_on_the_web_or_while_unbound(string config, string hostnames)
    {
        var result = await RunDeployAsync("Succeeded", config: config, hostnames: hostnames);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("azd provision --no-prompt", Calls());
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

    [UnixFact]
    public async Task Web_models_image_is_built_from_the_verified_files_and_pushed_when_the_registry_lacks_it()
    {
        var result = await RunWebModelsAsync(image: "MISSING");

        Assert.Equal(0, result.ExitCode);
        var image = $"sepiaboxacr.azurecr.io/sepiabox-web-models:{ModelsTag()}";
        Assert.Equal(image, result.Output.Trim());
        Assert.Contains(
            $"dotnet run --project src/Sepiabox.Ingest --configuration Release -- models download --models {Models} --only clip-vit-b32-text,clip-bpe-vocab,clip-vit-b32-vision",
            Calls());
        Assert.Contains(
            Calls(),
            call => call.StartsWith(
                $"docker build --platform linux/amd64 --provenance=false --sbom=false --build-arg BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet@sha256:0123abcd --file infra/web-models/Dockerfile --tag {image} ",
                StringComparison.Ordinal));
        Assert.Equal($"docker push --quiet {image}", Calls()[^1]);
        // Exactly the three files the web reads and the list that pins them.
        Assert.Equal(
            ["clip-bpe-simple-vocab-16e6.txt.gz", "clip-vit-b32-text.onnx", "clip-vit-b32-vision.onnx", "models.json"],
            File.ReadAllLines(_calls + ".context").Order(StringComparer.Ordinal));
    }

    [UnixFact]
    public async Task Web_models_image_is_reused_without_a_download_when_the_registry_has_it()
    {
        var result = await RunWebModelsAsync(image: "PRESENT");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"sepiaboxacr.azurecr.io/sepiabox-web-models:{ModelsTag()}", result.Output.Trim());
        Assert.DoesNotContain(Calls(), call => call.StartsWith("dotnet", StringComparison.Ordinal) || call.StartsWith("docker build", StringComparison.Ordinal));
    }

    [UnixFact]
    public async Task Web_models_image_is_not_built_when_a_download_does_not_verify()
    {
        var result = await RunWebModelsAsync(image: "MISSING", download: "FAIL");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("docker build", StringComparison.Ordinal) || call.StartsWith("docker push", StringComparison.Ordinal));
    }

    [UnixFact]
    public async Task Deploy_stops_before_touching_the_queue_or_the_schema_when_the_models_image_cannot_be_built()
    {
        var result = await RunDeployAsync("Succeeded", modelsImage: "MISSING", download: "FAIL");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("azd deploy", StringComparison.Ordinal));
        Assert.DoesNotContain(PauseQueue, Calls());
    }

    [UnixFact]
    public async Task Web_models_tag_is_the_same_for_a_models_json_checked_out_with_crlf()
    {
        // A copy of the script beside a models.json with Windows line ends, as a checkout with core.autocrlf gives.
        var root = Path.Combine(_stubs, "crlf-checkout");
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        Directory.CreateDirectory(Path.Combine(root, "models"));
        File.Copy(Path.Combine(Repository.Root, "scripts", "web-models.sh"), Path.Combine(root, "scripts", "web-models.sh"));
        var lf = File.ReadAllText(Path.Combine(Repository.Root, "models", "models.json"));
        Assert.DoesNotContain('\r', lf);
        File.WriteAllText(Path.Combine(root, "models", "models.json"), lf.Replace("\n", "\r\n", StringComparison.Ordinal));

        var result = await RunWebModelsAsync(image: "PRESENT", script: Path.Combine(root, "scripts", "web-models.sh"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"sepiaboxacr.azurecr.io/sepiabox-web-models:{ModelsTag()}", result.Output.Trim());
    }

    [UnixFact]
    public async Task Web_models_uses_the_ingest_s_default_folder_without_sepiabox_models()
    {
        var home = Path.Combine(_stubs, "home");
        Directory.CreateDirectory(Path.Combine(home, ".sepiabox", "models"));
        foreach (var file in Directory.GetFiles(Models))
        {
            File.Copy(file, Path.Combine(home, ".sepiabox", "models", Path.GetFileName(file)));
        }

        var result = await RunWebModelsAsync(image: "MISSING", models: null, home: home);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(Calls(), call => call.Contains($"--models {Path.Combine(home, ".sepiabox", "models")} ", StringComparison.Ordinal));
    }

    // The tag the script gives: the first 16 hex digits of the SHA-256 of models.json followed by the base digest.
    private static string ModelsTag()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Repository.Root, "models", "models.json"))
            .Concat(System.Text.Encoding.UTF8.GetBytes("mcr.microsoft.com/dotnet/aspnet@sha256:0123abcd"))
            .ToArray();
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
    }

    private Task<ProcessResult> RunWebModelsAsync(
        string image, string download = "", string? script = null, string? models = "default", string? home = null)
    {
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = _stubs + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["CALLS"] = _calls,
            ["STUB_MODELS_IMAGE"] = image,
            ["STUB_DOWNLOAD"] = download,
            ["SEPIABOX_MODELS"] = models == "default" ? Models : "",
            ["LOCALAPPDATA"] = "",
        };
        if (home is not null)
        {
            environment["HOME"] = home;
        }

        return RunAsync("bash", [script ?? Path.Combine(Repository.Root, "scripts", "web-models.sh")], environment);
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
        string resume = "",
        string modelsImage = "PRESENT",
        string download = "",
        string registry = "",
        string config = "",
        string dotenv = "",
        string hostnames = "",
        Dictionary<string, string>? extra = null) =>
        RunAsync("bash", [Path.Combine(Repository.Root, "scripts", "deploy.sh")], With(extra, new()
        {
            ["SEPIABOX_REGISTRY"] = registry,
            ["PATH"] = _stubs + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["CALLS"] = _calls,
            ["STUB_STATUSES"] = statuses,
            ["STUB_ENV_NAME"] = environmentName,
            ["STUB_LOCATION"] = location,
            ["STUB_RESOURCE_GROUP"] = resourceGroup,
            ["STUB_PG_STATE"] = postgresState,
            ["STUB_CONFIG"] = config,
            ["STUB_DOTENV"] = dotenv,
            ["STUB_HOSTNAMES"] = hostnames,
            ["STUB_EXECUTION"] = execution,
            ["STUB_STOP"] = stop,
            ["STUB_FAILING_DEPLOY"] = failingDeploy,
            ["STUB_RESUME"] = resume,
            ["SEPIABOX_MODELS"] = Models,
            ["STUB_MODELS_IMAGE"] = modelsImage,
            ["STUB_DOWNLOAD"] = download,
            ["SEPIABOX_MIGRATE_TIMEOUT_SECONDS"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SEPIABOX_MIGRATE_POLL_SECONDS"] = "0.1",
        }));

    private static Dictionary<string, string> With(Dictionary<string, string>? extra, Dictionary<string, string> environment)
    {
        foreach (var (name, value) in extra ?? [])
        {
            environment[name] = value;
        }

        return environment;
    }

    private static async Task<ProcessResult> RunAsync(string fileName, string[] arguments, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The deploy script refuses these (D-072): none may leak in from the shell that runs the tests.
        foreach (var name in new[] { "AZURE_CUSTOM_DOMAIN", "AZURE_CUSTOM_DOMAIN_BINDING", "AZURE_UPLOADER_PRINCIPAL_IDS" })
        {
            start.Environment.Remove(name);
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
