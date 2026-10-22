using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sepiabox.AppHost.Tests;

/// <summary>
/// The infrastructure azd provisions from the AppHost (S-032), checked on the published Bicep.
/// </summary>
public sealed class AzurePublishTests(PublishedInfrastructure published) : IClassFixture<PublishedInfrastructure>
{
    private const string PostgresServer = "Microsoft.DBforPostgreSQL/flexibleServers";
    private const string ContainerApp = "Microsoft.App/containerApps";
    private const string ContainerAppJob = "Microsoft.App/jobs";

    [Fact]
    public void Postgres_is_a_burstable_B1ms_server_with_32_GB_and_no_auto_grow_when_published()
    {
        var server = published.The(PostgresServer);
        var sku = server.Block("sku");
        var storage = server.Block("storage");

        Assert.Contains("name: 'Standard_B1ms'", sku, StringComparison.Ordinal);
        Assert.Contains("tier: 'Burstable'", sku, StringComparison.Ordinal);
        Assert.Contains("storageSizeGB: 32", storage, StringComparison.Ordinal);
        Assert.Contains("autoGrow: 'Disabled'", storage, StringComparison.Ordinal);
        Assert.Equal("'17'", server.Property("version"));
        Assert.Equal("'francecentral'", server.Property("location"));
    }

    [Fact]
    public void Postgres_allows_the_vector_and_postgis_extensions_when_published()
    {
        var configuration = Assert.Single(
            published.Declarations("Microsoft.DBforPostgreSQL/flexibleServers/configurations"));

        Assert.Equal("'azure.extensions'", configuration.Property("name"));
        Assert.Equal("'VECTOR,POSTGIS'", configuration.Property("value"));
    }

    [Fact]
    public void Postgres_credentials_are_generated_without_characters_that_break_the_connection_string()
    {
        // Aspire writes the password unquoted into `Password=...;`, so a `;` or a quote would break it.
        var password = Generate("pg-password");
        var login = Generate("pg-username");

        Assert.True(published.Resource("pg-password")["inputs"]!["value"]!["secret"]!.GetValue<bool>());
        Assert.False(password["special"]!.GetValue<bool>());
        Assert.Equal(64, password["minLength"]!.GetValue<int>());
        Assert.Equal(1, password["minLower"]!.GetValue<int>());
        Assert.Equal(1, password["minUpper"]!.GetValue<int>());
        Assert.Equal(1, password["minNumeric"]!.GetValue<int>());
        Assert.False(login["special"]!.GetValue<bool>());
        Assert.False(login["numeric"]!.GetValue<bool>());
        Assert.False(login["upper"]!.GetValue<bool>());
    }

    [Fact]
    public void Postgres_firewall_has_only_the_azure_services_rule_when_published()
    {
        // Accepted exposure (D-061): any Azure tenant reaches port 5432 and must still know the password.
        // A change here must be deliberate.
        var rule = Assert.Single(published.Declarations("Microsoft.DBforPostgreSQL/flexibleServers/firewallRules"));

        Assert.Equal("'AllowAllAzureIps'", rule.Property("name"));
        Assert.Equal("'0.0.0.0'", rule.Property("startIpAddress"));
        Assert.Equal("'0.0.0.0'", rule.Property("endIpAddress"));
    }

    [Fact]
    public void No_module_outputs_a_password_or_a_key_when_published()
    {
        var outputs = published.Modules.Values
            .SelectMany(module => module.Split('\n'))
            .Where(line => line.StartsWith("output ", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(outputs);
        Assert.DoesNotContain(outputs, line => line.Contains("administratorLoginPassword", StringComparison.Ordinal));
        Assert.DoesNotContain(outputs, line => line.Contains("listKeys", StringComparison.Ordinal));
    }

    [Fact]
    public void Storage_takes_no_shared_keys_and_keeps_deleted_blobs_for_a_week_when_published()
    {
        var account = published.The("Microsoft.Storage/storageAccounts");
        var retention = published.The("Microsoft.Storage/storageAccounts/blobServices").Block("deleteRetentionPolicy");

        Assert.Equal("false", account.Property("allowSharedKeyAccess"));
        Assert.Contains("enabled: true", retention, StringComparison.Ordinal);
        Assert.Contains("days: 7", retention, StringComparison.Ordinal);
    }

    [Fact]
    public void Storage_deletes_blobs_left_in_incoming_after_thirty_days_and_nothing_else_when_published()
    {
        var policy = published.The("Microsoft.Storage/storageAccounts/managementPolicies");

        Assert.Equal("'default'", policy.Property("name"));
        Assert.Contains("'incoming/'", policy.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("'originals/'", policy.Body, StringComparison.Ordinal);
        Assert.Contains("daysAfterModificationGreaterThan: 30", policy.Body, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(policy.Body, "delete:"));
        Assert.Equal(30, AzureInfrastructure.IncomingRetentionDays);
    }

    [Fact]
    public void Storage_blocks_public_access_and_has_every_private_container_when_published()
    {
        var account = published.The("Microsoft.Storage/storageAccounts");
        var containers = published.Declarations("Microsoft.Storage/storageAccounts/blobServices/containers");

        Assert.Equal("false", account.Property("allowBlobPublicAccess"));
        Assert.Equal("'francecentral'", account.Property("location"));
        Assert.Equal(
            ["dataprotection", "derivatives", "exports", "incoming", "manifests", "originals", "tokenring"],
            containers.Select(c => c.Property("name")!.Trim('\'')).Order(StringComparer.Ordinal));
        Assert.All(containers, c => Assert.Equal("'None'", c.Property("publicAccess")));
    }

    [Fact]
    public void Every_resource_is_in_France_Central_when_published()
    {
        // A resource left on the `location` parameter would follow whatever region the azd environment names.
        var located = published.Modules.Values
            .SelectMany(BicepResource.Parse)
            .Where(r => !r.IsExisting && r.Property("location") is not null)
            .ToList();

        Assert.NotEmpty(located);
        Assert.All(located, r => Assert.True(
            r.Property("location") == "'francecentral'", $"{r.Identifier} ({r.Type}) is in {r.Property("location")}."));
    }

    [Fact]
    public void Web_scales_to_zero_with_at_most_one_replica_when_published()
    {
        var scale = WebApp().Block("scale");

        Assert.Contains("minReplicas: 0", scale, StringComparison.Ordinal);
        Assert.Contains("maxReplicas: 1", scale, StringComparison.Ordinal);
    }

    [Fact]
    public void Web_is_reached_only_through_the_https_ingress_when_published()
    {
        var web = WebApp();
        var ingress = web.Block("ingress");

        Assert.Contains("external: true", ingress, StringComparison.Ordinal);
        Assert.Contains("allowInsecure: false", ingress, StringComparison.Ordinal);
        Assert.Contains("transport: 'http'", ingress, StringComparison.Ordinal);
        Assert.DoesNotContain("exposedPort", ingress, StringComparison.Ordinal);
        Assert.DoesNotContain("additionalPortMappings", ingress, StringComparison.Ordinal);
        Assert.DoesNotContain("ipSecurityRestrictions", ingress, StringComparison.Ordinal);
        // The ingress is the only way in, so no other app or job exposes anything.
        Assert.All(
            published.Declarations(ContainerApp).Concat(published.Declarations(ContainerAppJob)).Where(r => r != web),
            r => Assert.DoesNotContain("ingress", r.Body, StringComparison.Ordinal));
    }

    [Fact]
    public void Web_accepts_only_the_container_apps_default_host_and_the_custom_domain_when_published()
    {
        var allowedHosts = Regex.Match(
            WebApp().Body, @"name: 'AllowedHosts'\s+value: '(?<value>[^']+)'", RegexOptions.Singleline);

        Assert.True(allowedHosts.Success, "The web has no AllowedHosts setting.");
        Assert.Equal(
            // An empty custom domain leaves an empty entry, which host filtering ignores (TransportSecurityTests).
            "sepiabox-web.${aca_outputs_azure_container_apps_environment_default_domain};${customdomain_value}",
            allowedHosts.Groups["value"].Value);
        Assert.Equal(
            "{customDomain.value}", published.ModuleParameters["web-containerapp.module.bicep"]["customdomain_value"]!.GetValue<string>());
    }

    [Fact]
    public void Resource_names_are_the_ones_the_shutdown_runbook_stops_when_published()
    {
        Assert.Equal(Quoted(ShutdownRunbook.WebApp), published.The(ContainerApp).Property("name"));
        Assert.Equal(Quoted(ShutdownRunbook.PostgresServer), published.The(PostgresServer).Property("name"));
    }

    [Fact]
    public void Migrations_run_as_a_manual_job_of_the_worker_when_published()
    {
        var job = published.Named(ContainerAppJob, "sepiabox-migrate");
        var migrate = published.Resource("migrate");

        Assert.Equal("'sepiabox-migrate'", job.Property("name"));
        Assert.Equal("'Manual'", job.Property("triggerType"));
        Assert.Equal("600", job.Property("replicaTimeout"));
        Assert.Equal("0", job.Property("replicaRetryLimit"));
        Assert.Contains("parallelism: 1", job.Block("manualTriggerConfig"), StringComparison.Ordinal);
        Assert.Matches(@"args: \[\s+'migrate'\s+\]", job.Body);
        Assert.EndsWith("Sepiabox.Worker.csproj", migrate["path"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("migrate", Assert.Single(migrate["args"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public void No_worker_is_published_as_an_always_on_container_app()
    {
        // Published as projects, the run-once workers would become always-on Container Apps (D-034).
        var names = published.Manifest["resources"]!.AsObject().Select(r => r.Key).ToList();

        Assert.Contains("web", names);
        Assert.DoesNotContain(names, name => name.StartsWith("worker", StringComparison.Ordinal));
        Assert.Equal("'sepiabox-web'", Assert.Single(published.Declarations(ContainerApp)).Property("name"));
    }

    [Fact]
    public void Apps_get_their_catalog_connections_as_secrets_never_as_plain_values_when_published()
    {
        var web = WebApp();
        var job = published.Named(ContainerAppJob, "sepiabox-migrate");

        // The job, as the administrator, reads its connection from the database vault (D-062).
        Assert.Matches(@"name: 'ConnectionStrings__catalog'\s+secretRef: 'connectionstrings--catalog'", job.Body);
        Assert.Matches(@"name: 'connectionstrings--catalog'\s+identity: \w+\s+keyVaultUrl: database_vault_", job.Body);
        // The web connects as the application role, from a Container Apps secret built from a secure parameter.
        Assert.Matches(@"name: 'ConnectionStrings__catalog'\s+secretRef: 'connectionstrings--catalog'", web.Body);
        Assert.Contains("@secure()\nparam pg_app_password_value string", published.Modules["web-containerapp.module.bicep"], StringComparison.Ordinal);
        Assert.All(new[] { web, job }, app =>
            // The administrator's password parameter is never written into an app.
            Assert.DoesNotContain("pg_password_value", app.Body, StringComparison.Ordinal));
    }

    [Fact]
    public void Telemetry_goes_to_application_insights_on_the_capped_workspace_when_published()
    {
        var insights = published.The("Microsoft.Insights/components");
        var workspaces = published.Modules.Values.SelectMany(BicepResource.Parse)
            .Where(r => r.Type == "Microsoft.OperationalInsights/workspaces")
            .ToList();

        Assert.Equal("logs_outputs_loganalyticsworkspaceid", insights.Property("WorkspaceResourceId"));
        // The workspace with the daily cap is sepiabox-infra's (S-031); publishing never creates another.
        Assert.NotEmpty(workspaces);
        Assert.All(workspaces, w =>
        {
            Assert.True(w.IsExisting);
            Assert.Equal("'sepiabox-logs'", w.Property("name"));
        });
        Assert.Contains(
            "APPLICATIONINSIGHTS_CONNECTION_STRING", published.Resource("web")["env"]!.AsObject().Select(e => e.Key));
    }

    [Fact]
    public void Registry_is_basic_and_the_environment_has_no_managed_dashboard_when_published()
    {
        var registry = published.The("Microsoft.ContainerRegistry/registries");

        Assert.Contains("name: 'Basic'", registry.Block("sku"), StringComparison.Ordinal);
        Assert.Single(published.Declarations("Microsoft.App/managedEnvironments"));
        Assert.Empty(published.Declarations("Microsoft.App/managedEnvironments/dotNetComponents"));
        // The owner's secrets, and the database connections only the migrate job reads (D-062).
        Assert.Equal(2, published.Declarations("Microsoft.KeyVault/vaults").Count);
    }

    [Fact]
    public void No_parameter_publishes_a_default_value_that_would_hide_its_azd_config()
    {
        // azd resolves a parameter with a published default.value from AZURE_<NAME> or the default alone, and
        // never from the environment's infra.parameters, where the owner and the Deploy workflow set them (D-072).
        // Generated defaults (the PostgreSQL passwords) are read from infra.parameters and are fine.
        var parameters = published.Manifest["resources"]!.AsObject()
            .Where(r => r.Value!["type"]?.GetValue<string>() == "parameter.v0")
            .ToList();
        var withValue = parameters
            .Where(r => r.Value!["inputs"]?["value"]?["default"]?["value"] is not null)
            .Select(r => r.Key);

        // Guards the check itself: a manifest read wrongly would find no parameters and pass.
        Assert.Equal(
            ["customDomain", "customDomainBinding", "pg-app-password", "pg-password", "pg-username", "uploaderPrincipalIds"],
            parameters.Select(r => r.Key).Order(StringComparer.Ordinal));
        Assert.Empty(withValue);
    }

    private JsonObject Generate(string parameter) =>
        published.Resource(parameter)["inputs"]!["value"]!["default"]!["generate"]!.AsObject();

    private BicepResource WebApp() => published.The(ContainerApp);

    private static string Quoted(string value) => "'" + value + "'";
}
