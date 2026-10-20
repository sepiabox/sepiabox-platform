using System.Text.RegularExpressions;

namespace Sepiabox.AppHost.Tests;

/// <summary>
/// Identities, roles, secrets and the key ring the apps get in Azure (S-058), checked on the published Bicep.
/// </summary>
public sealed partial class AzureAccessTests(PublishedInfrastructure published) : IClassFixture<PublishedInfrastructure>
{
    private const string RoleAssignment = "Microsoft.Authorization/roleAssignments";

    private static readonly Dictionary<string, string> s_roleNames = new(StringComparer.Ordinal)
    {
        ["2a2b9908-6ea1-4ae2-8e65-a410df84e7d1"] = "Storage Blob Data Reader",
        ["ba92f5b4-2d11-453d-a403-e96b0029c9fe"] = "Storage Blob Data Contributor",
        ["4633458b-17de-408a-b874-0445c86b69e6"] = "Key Vault Secrets User",
        ["12338af0-0e69-4776-bea7-57ae8d297424"] = "Key Vault Crypto User",
        ["7f951dda-4ed3-4680-a7ca-43fe172d538d"] = "AcrPull",
        ["69a216fc-b8fb-44d8-bc22-1f3c2cd27a39"] = "Azure Service Bus Data Sender",
        ["4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0"] = "Azure Service Bus Data Receiver",
        ["090c5cfd-751d-490a-894a-3ce6f1109419"] = "Azure Service Bus Data Owner",
    };

    [Fact]
    public void Every_identity_has_exactly_the_roles_it_needs_when_published()
    {
        var assignments = Assignments().Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            new[]
            {
                // The Container Apps environment's identity pulls the images.
                "aca_mi.properties.principalId: AcrPull on registries registry",
                // The uploaders, one assignment per id in uploaderPrincipalIds.
                "id: Storage Blob Data Contributor on storageAccounts storage",
                // The import jobs read blobs and the queue; KEDA reads its length with the Manage claim (D-063).
                "import-identity: Azure Service Bus Data Owner on queues manifests",
                "import-identity: Storage Blob Data Reader on storageAccounts storage",
                // The migrate job reads only the database vault (its connection).
                "migrate-identity: Key Vault Secrets User on vaults database-vault",
                // Event Grid's system topic delivers storage events to the queue (S-033).
                "storageEvents.identity.principalId: Azure Service Bus Data Sender on queues manifests",
                // The web reads blobs, writes only the key ring, reads the owner's secrets and uses one key.
                "web-identity: Key Vault Crypto User on keys dataprotection",
                "web-identity: Key Vault Secrets User on vaults vault",
                "web-identity: Storage Blob Data Contributor on containers dataprotection",
                "web-identity: Storage Blob Data Reader on storageAccounts storage",
            }.Order(StringComparer.Ordinal),
            assignments);
    }

    [Fact]
    public void Each_uploader_id_gets_blob_contributor_on_the_account_when_published()
    {
        var uploaders = Assert.Single(published.Declarations(RoleAssignment), r => r.IsLoop);
        var parameter = published.Resource("uploaderPrincipalIds");

        Assert.Matches(@"^\s*for id in uploaders: \{", uploaders.Body);
        Assert.Equal("'User'", uploaders.Property("principalType"));
        // Trimmed, lower-cased, without empty entries or repeats.
        Assert.Contains(
            "var uploaderIds = filter(map(split(uploaderPrincipalIds, ','), id => toLower(trim(id))), id => !empty(id))",
            published.Modules["access.bicep"],
            StringComparison.Ordinal);
        Assert.Contains(
            "var uploaders = filter(uploaderIds, (id, i) => indexOf(uploaderIds, id) == i)",
            published.Modules["access.bicep"],
            StringComparison.Ordinal);
        Assert.Equal("{uploaderPrincipalIds.value}", published.ModuleParameters["access.bicep"]["uploaderPrincipalIds"]!.GetValue<string>());
        // Empty unless the owner sets it, so a first deploy needs no answer (docs/operations.md).
        Assert.Equal("", parameter["inputs"]!["value"]!["default"]!["value"]!.GetValue<string>());
        Assert.False(parameter["inputs"]!["value"]!["secret"]?.GetValue<bool>() ?? false);
    }

    [Fact]
    public void Owner_secrets_reach_only_the_web_and_only_from_key_vault_when_published()
    {
        var webEnvironment = Environment("web");
        var jobKeys = new[] { "migrate", "import", "import-all", "geonames" }.SelectMany(job => Environment(job).Keys).ToList();

        // Key Vault is the web's configuration source: Auth--Google--ClientId is Auth:Google:ClientId (D-051).
        Assert.Equal("{vault.connectionString}", webEnvironment["ConnectionStrings__vault"]);
        Assert.DoesNotContain("ConnectionStrings__vault", jobKeys);
        string[] ownerSecrets = ["Auth__", "Access__", "Anthropic__"];
        Assert.DoesNotContain(
            webEnvironment.Keys.Concat(jobKeys),
            key => ownerSecrets.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)));
        // The owner creates the secrets; the deploy never writes one into the owner's vault.
        Assert.DoesNotContain(
            published.Declarations("Microsoft.KeyVault/vaults/secrets"),
            secret => !secret.Body.Contains("database_vault", StringComparison.Ordinal)
                && !secret.Body.Contains("parent: keyVault", StringComparison.Ordinal));
    }

    [Fact]
    public void Web_takes_today_in_lisbon_for_on_this_day_when_published()
    {
        // S-013, D-069: the container's clock is UTC; the family's day is Lisbon's.
        Assert.Equal("Europe/Lisbon", Environment("web")["Search__TimeZone"]);
    }

    // The web runs in a Linux container; with invariant globalization Windows cannot resolve IANA ids.
    [UnixFact("IANA time zone ids need ICU or tzdata, which the invariant-globalization Windows run lacks.")]
    public void Search_time_zone_resolves_when_the_web_runs_on_linux()
    {
        Assert.True(TimeZoneInfo.TryFindSystemTimeZoneById(AzureInfrastructure.SearchTimeZone, out _));
    }

    [Fact]
    public void Web_has_exactly_one_data_protection_key_ring_when_published()
    {
        var web = published.The("Microsoft.App/containerApps");
        var key = published.The("Microsoft.KeyVault/vaults/keys");

        // Container Apps' managed data protection is off; the web's own ring in Blob Storage is on (D-062).
        Assert.Contains("autoConfigureDataProtection: false", web.Block("runtime"), StringComparison.Ordinal);
        Assert.Equal("{access.outputs.dataProtectionKeyUri}", Environment("web")["DataProtection__KeyUri"]);
        Assert.Equal("'dataprotection'", key.Property("name"));
        Assert.Equal("'RSA'", key.Property("kty"));
        Assert.Matches(@"keyOps: \[\s+'wrapKey'\s+'unwrapKey'\s+\]", key.Body);
        Assert.Contains("dataprotection", AzureInfrastructure.BlobContainers);
    }

    [Fact]
    public void Web_connects_to_the_catalog_as_the_application_role_when_published()
    {
        var connection = Environment("web")["ConnectionStrings__catalog"];
        var job = Environment("migrate");

        Assert.Equal(
            "Host=sepiabox-pg.postgres.database.azure.com;Username=sepiabox_web;Password={pg-app-password.value};Database=catalog",
            connection);
        Assert.Equal("{catalog.connectionString}", job["ConnectionStrings__catalog"]);
        Assert.Equal("sepiabox_web", job["Catalog__AppRole__Name"]);
        Assert.Equal("{pg-app-password.value}", job["Catalog__AppRole__Password"]);
        Assert.True(published.Resource("pg-app-password")["inputs"]!["value"]!["secret"]!.GetValue<bool>());
        Assert.False(published.Resource("pg-app-password")["inputs"]!["value"]!["default"]!["generate"]!["special"]!.GetValue<bool>());
        // The host name the web names is the one Azure gives the server it connects to.
        Assert.Equal("'sepiabox-pg'", published.The("Microsoft.DBforPostgreSQL/flexibleServers").Property("name"));
    }

    /// <summary>Every role assignment as "principal: role on scope-type scope-name".</summary>
    private IEnumerable<string> Assignments()
    {
        foreach (var (file, module) in published.Modules)
        {
            var resources = BicepResource.Parse(module).ToList();
            foreach (var assignment in resources.Where(r => r.Type == RoleAssignment))
            {
                var role = RoleId().Match(assignment.Body).Groups["id"].Value;
                var principal = Resolve(file, assignment.Property("principalId")!);
                var scope = resources.Single(r => r.Identifier == assignment.Property("scope"));
                var scopeType = scope.Type[(scope.Type.LastIndexOf('/') + 1)..];
                yield return $"{principal}: {s_roleNames.GetValueOrDefault(role, role)} on {scopeType} {Resolve(file, scope.Property("name")!)}";
            }
        }
    }

    /// <summary>A module parameter as the AppHost resource that fills it; a literal or expression as written.</summary>
    private string Resolve(string file, string expression)
    {
        if (published.ModuleParameters[file][expression]?.GetValue<string>() is { } value)
        {
            // "{web-identity.outputs.principalId}" or "{vault.outputs.name}"
            return value.TrimStart('{').Split('.')[0];
        }

        return expression.Trim('\'');
    }

    private Dictionary<string, string> Environment(string resource) =>
        published.Resource(resource)["env"]!.AsObject().ToDictionary(e => e.Key, e => e.Value!.GetValue<string>());

    [GeneratedRegex(@"roleDefinitions', '(?<id>[0-9a-f-]{36})'")]
    private static partial Regex RoleId();
}
