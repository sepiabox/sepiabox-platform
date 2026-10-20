using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Sepiabox.AppHost.Tests;

/// <summary>The deploy workflow (S-034, D-064), read as YAML: what triggers it, what it may do and in what order.</summary>
public sealed partial class DeployWorkflowTests
{
    private static readonly string s_path = Path.Combine(Repository.Root, ".github", "workflows", "deploy.yml");
    private readonly YamlMappingNode _workflow = Load(s_path);

    [Fact]
    public void Workflow_runs_after_ci_on_main_or_by_hand_and_never_on_a_pull_request()
    {
        var on = Map(_workflow, "on");

        Assert.Equal(["workflow_run", "workflow_dispatch"], on.Children.Keys.Select(Text));
        var afterCi = Map(on, "workflow_run");
        Assert.Equal(["CI"], Sequence(afterCi, "workflows"));
        Assert.Equal(["completed"], Sequence(afterCi, "types"));
        Assert.Equal(["main"], Sequence(afterCi, "branches"));
        // The whole condition, so a dropped clause or operator shows: only a green CI run of a push to main in
        // this repository (a workflow_run also follows CI runs of pull requests and forks), or a manual run of main.
        Assert.Equal(
            "(github.event_name == 'workflow_dispatch' && github.ref == 'refs/heads/main') || "
            + "(github.event_name == 'workflow_run' && github.event.workflow_run.conclusion == 'success' && "
            + "github.event.workflow_run.event == 'push' && "
            + "github.event.workflow_run.head_repository.full_name == github.repository)",
            Normalized(Text(CheckJob()["if"])));
        // The deploy job, and its approval, only follow a check that decided to deploy.
        Assert.Equal("check", Text(Job()["needs"]));
        Assert.Equal("needs.check.outputs.deploy == 'true'", Text(Job()["if"]));
    }

    [Fact]
    public void A_manual_run_deploys_only_from_main()
    {
        // Another branch's manual run would use that branch's copy of this file; the environment's deployment
        // branch rule (operations.md) refuses it too.
        Assert.Contains(
            "(github.event_name == 'workflow_dispatch' && github.ref == 'refs/heads/main')",
            Normalized(Text(CheckJob()["if"])),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_newest_commit_on_main_is_deployed_and_a_stale_run_never_asks_for_approval()
    {
        var check = CheckJob();

        // The check job needs no approval and no Azure rights: a stale or refused run ends there.
        Assert.False(check.Children.ContainsKey(new YamlScalarNode("environment")));
        Assert.Equal(
            new Dictionary<string, string> { ["contents"] = "read" },
            Map(check, "permissions").Children.ToDictionary(p => Text(p.Key), p => Text(p.Value)));
        Assert.Equal("${{ steps.newest.outputs.deploy }}", Text(Map(check, "outputs")["deploy"]));
        AssertNewestCommitCheck(Assert.Single(StepsOf(check)));

        // Checked again in the deploy job, after the approval, which can come much later; every later step
        // depends on it.
        var newest = Steps().Single(step => Name(step) == NewestStep);
        AssertNewestCommitCheck(newest);
        var index = Steps().IndexOf(newest);
        Assert.True(index < Steps().Select(Name).ToList().IndexOf("Deploy"));
        Assert.All(Steps().Skip(index + 1), step => Assert.Equal("steps.newest.outputs.deploy == 'true'", Text(step["if"])));
    }

    [Fact]
    public void Job_has_a_time_limit_and_the_checkout_keeps_no_token()
    {
        var checkout = Steps().Single(step => Uses(step).StartsWith("actions/checkout@", StringComparison.Ordinal));

        Assert.Equal("60", Text(Job()["timeout-minutes"]));
        Assert.Equal("false", Text(Map(checkout, "with")["persist-credentials"]));
    }

    [Fact]
    public void No_workflow_in_the_repository_can_be_started_by_a_pull_request_with_secrets()
    {
        // pull_request_target runs with the repository's secrets on a fork's code; no workflow may use it, and
        // the only workflow with a pull_request trigger (CI) has no id-token and no environment.
        foreach (var file in Directory.GetFiles(Path.Combine(Repository.Root, ".github", "workflows"), "*.yml"))
        {
            var workflow = Load(file);
            var triggers = Map(workflow, "on").Children.Keys.Select(Text).ToList();
            Assert.DoesNotContain("pull_request_target", triggers);
            if (triggers.Contains("pull_request"))
            {
                Assert.DoesNotContain("id-token", Map(workflow, "permissions").Children.Keys.Select(Text));
                Assert.DoesNotContain("secrets.", File.ReadAllText(file), StringComparison.Ordinal);
                Assert.DoesNotContain("environment:", File.ReadAllText(file), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Workflow_has_only_the_permissions_oidc_needs_and_runs_in_the_production_environment()
    {
        var permissions = Map(_workflow, "permissions");

        Assert.Equal(
            new Dictionary<string, string> { ["id-token"] = "write", ["contents"] = "read" },
            permissions.Children.ToDictionary(p => Text(p.Key), p => Text(p.Value)));
        Assert.False(Job().Children.ContainsKey(new YamlScalarNode("permissions")), "The job must not widen the permissions.");
        Assert.Equal("production", Text(Job()["environment"]));
        Assert.Equal("false", Text(Map(_workflow, "concurrency")["cancel-in-progress"]));
    }

    [Fact]
    public void Workflow_signs_in_with_oidc_from_the_repository_variables()
    {
        var login = Steps().Single(step => Uses(step).StartsWith("azure/login@", StringComparison.Ordinal));
        var with = Map(login, "with");

        Assert.Equal("${{ vars.AZURE_CLIENT_ID }}", Text(with["client-id"]));
        Assert.Equal("${{ vars.AZURE_TENANT_ID }}", Text(with["tenant-id"]));
        Assert.Equal("${{ vars.AZURE_SUBSCRIPTION_ID }}", Text(with["subscription-id"]));
        Assert.False(with.Children.ContainsKey(new YamlScalarNode("creds")), "OIDC only: no service principal secret.");
        Assert.Contains("--federated-credential-provider github", Run("Sign azd in with the same federated credential"), StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_restores_the_saved_database_parameters_so_azd_never_generates_new_ones()
    {
        var step = Steps().Single(s => Name(s) == "Create the azd environment from the saved parameters");
        var environment = Map(step, "env");
        var run = Run("Create the azd environment from the saved parameters");

        foreach (var (secret, parameter) in new[]
        {
            ("PG_USERNAME", "pg_username"), ("PG_PASSWORD", "pg_password"), ("PG_APP_PASSWORD", "pg_app_password"),
        })
        {
            Assert.Equal("${{ secrets." + secret + " }}", Text(environment[secret]));
            Assert.Contains($"azd env config set infra.parameters.{parameter} \"${secret}\"", run, StringComparison.Ordinal);
        }

        Assert.Contains("azd env new sepiabox --location francecentral", run, StringComparison.Ordinal);
        // The custom domain (S-035) comes from variables, with no domain and a disabled binding when unset.
        Assert.Equal("${{ vars.CUSTOM_DOMAIN }}", Text(environment["CUSTOM_DOMAIN"]));
        Assert.Equal("${{ vars.CUSTOM_DOMAIN_BINDING }}", Text(environment["CUSTOM_DOMAIN_BINDING"]));
        Assert.Contains("azd env config set infra.parameters.customDomain \"$CUSTOM_DOMAIN\"", run, StringComparison.Ordinal);
        Assert.Contains(
            "azd env config set infra.parameters.customDomainBinding \"${CUSTOM_DOMAIN_BINDING:-Disabled}\"", run, StringComparison.Ordinal);
        // A missing secret, or jq's "null" saved as one, stops the run before azd could generate a value.
        Assert.Contains("if [[ -z \"${!name}\" || \"${!name}\" == \"null\" ]]; then", run, StringComparison.Ordinal);
        Assert.Contains("exit 1", run, StringComparison.Ordinal);
        Assert.True(
            run.IndexOf("exit 1", StringComparison.Ordinal) < run.IndexOf("azd env new", StringComparison.Ordinal),
            "The secrets are checked before the environment is created.");
    }

    [Fact]
    public void Workflow_deploys_with_the_script_that_migrates_first_and_purges_old_images_only_after_success()
    {
        var names = Steps().Select(Name).ToList();
        var deploy = names.IndexOf("Deploy");
        var purge = names.IndexOf("Purge old images");

        // deploy.sh runs the migrate job and stops before the web when it fails (DeployScriptTests).
        Assert.Equal("scripts/deploy.sh", Run("Deploy"));
        Assert.True(deploy >= 0 && purge > deploy, "The purge comes after the deploy.");
        // No step runs after a failure (their only condition is the newest-commit check) or ignores one.
        Assert.DoesNotContain(Steps(), step => step.Children.ContainsKey(new YamlScalarNode("if"))
            && Text(step["if"]) != "steps.newest.outputs.deploy == 'true'");
        Assert.DoesNotContain(Steps(), step => step.Children.ContainsKey(new YamlScalarNode("continue-on-error")));
        // A bare azd deploy would skip the migrate job (and the predeploy hook refuses it).
        Assert.DoesNotContain("azd deploy", File.ReadAllText(s_path), StringComparison.Ordinal);
    }

    [Fact]
    public void Purge_keeps_the_last_five_images_of_every_repository_in_the_registry()
    {
        var run = Run("Purge old images");

        Assert.Contains("az acr repository list --name \"$registry\"", run, StringComparison.Ordinal);
        Assert.Matches(PurgeCommand(), run);
    }

    [Fact]
    public void Ci_lints_the_workflows_with_actionlint_and_the_scripts_with_shellcheck()
    {
        var ci = File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "ci.yml"));

        Assert.Matches(@"actionlint_\d+\.\d+\.\d+_linux_amd64\.tar\.gz", ci);
        Assert.Contains("sha256sum --check", ci, StringComparison.Ordinal);
        Assert.Contains("shellcheck scripts/*.sh", ci, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"az acr run --registry ""\$registry"" \\\s+--cmd ""acr purge --filter '\$\{repository\}:\.\*' --ago 0d --keep 5 --untagged"" /dev/null")]
    private static partial Regex PurgeCommand();

    private const string NewestStep = "Skip a commit that is no longer the newest on main";

    private YamlMappingNode Job() => Map(Map(_workflow, "jobs"), "deploy");

    private YamlMappingNode CheckJob() => Map(Map(_workflow, "jobs"), "check");

    private List<YamlMappingNode> Steps() => StepsOf(Job());

    private static List<YamlMappingNode> StepsOf(YamlMappingNode job) =>
        [.. ((YamlSequenceNode)job["steps"]).Children.Cast<YamlMappingNode>()];

    private static void AssertNewestCommitCheck(YamlMappingNode step)
    {
        var run = Text(step["run"]);

        Assert.Equal(NewestStep, Name(step));
        Assert.Equal("newest", Text(step["id"]));
        Assert.Equal("${{ github.event.workflow_run.head_sha || github.sha }}", Text(Map(step, "env")["DEPLOY_SHA"]));
        Assert.Contains("latest=$(git ls-remote \"$REPOSITORY_URL\" refs/heads/main | cut -f1)", run, StringComparison.Ordinal);
        Assert.Contains("if [[ \"$latest\" == \"$DEPLOY_SHA\" ]]", run, StringComparison.Ordinal);
        Assert.Contains("echo \"deploy=false\" >> \"$GITHUB_OUTPUT\"", run, StringComparison.Ordinal);
    }

    private string Run(string name) => Text(Steps().Single(step => Name(step) == name)["run"]).Trim();

    private static string Name(YamlMappingNode step) =>
        step.Children.TryGetValue(new YamlScalarNode("name"), out var name) ? Text(name) : "";

    private static string Uses(YamlMappingNode step) =>
        step.Children.TryGetValue(new YamlScalarNode("uses"), out var uses) ? Text(uses) : "";

    private static YamlMappingNode Load(string path)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(File.ReadAllText(path));
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlMappingNode Map(YamlMappingNode node, string key) => (YamlMappingNode)node[key];

    private static List<string> Sequence(YamlMappingNode node, string key) =>
        [.. ((YamlSequenceNode)node[key]).Children.Select(Text)];

    private static string Text(YamlNode node) => ((YamlScalarNode)node).Value ?? "";

    private static string Normalized(string expression) => Whitespace().Replace(expression, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
