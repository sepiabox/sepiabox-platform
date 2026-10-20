using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sepiabox.AppHost.Tests;

/// <summary>
/// Publishes the AppHost once, as <c>azd</c> does (the manifest and one Bicep module per Azure resource),
/// into a temporary folder. Nothing is started or deployed, so it needs neither Docker nor Azure.
/// </summary>
public sealed class PublishedInfrastructure : IAsyncLifetime
{
    private static readonly TimeSpan s_publishTimeout = TimeSpan.FromMinutes(2);

    public string OutputPath { get; } = Path.Combine(Path.GetTempPath(), "sepiabox-publish-" + Guid.NewGuid().ToString("N"));

    /// <summary>The Aspire manifest (<c>aspire-manifest.json</c>) azd reads.</summary>
    public JsonObject Manifest { get; private set; } = null!;

    /// <summary>Every generated Bicep module, by file name.</summary>
    public IReadOnlyDictionary<string, string> Modules { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(s_publishTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Sepiabox_AppHost>(
            ["--publisher", "manifest", "--output-path", OutputPath], cts.Token);
        await using var app = await builder.BuildAsync(cts.Token);
        await app.RunAsync(cts.Token);

        Manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(OutputPath, "aspire-manifest.json"), cts.Token))!
            .AsObject();
        Modules = Directory.GetFiles(OutputPath, "*.bicep")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllText, StringComparer.Ordinal);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(OutputPath))
        {
            Directory.Delete(OutputPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>The manifest entry of an AppHost resource.</summary>
    public JsonObject Resource(string name) =>
        Manifest["resources"]?[name]?.AsObject() ?? throw new KeyNotFoundException($"No resource '{name}' in the manifest.");

    /// <summary>Every resource declaration (not <c>existing</c>) of a type, across all modules.</summary>
    public IReadOnlyList<BicepResource> Declarations(string type) =>
        [.. Modules.Values.SelectMany(BicepResource.Parse).Where(r => !r.IsExisting && r.Type == type)];

    /// <summary>The one declaration of a type, failing when there is none or several.</summary>
    public BicepResource The(string type)
    {
        var declarations = Declarations(type);
        Assert.True(declarations.Count == 1, $"Expected one '{type}', found {declarations.Count}.");
        return declarations[0];
    }
}

/// <summary>
/// One <c>resource</c> declaration of a generated module: Aspire writes each one from a line
/// <c>resource name 'Type@version' = {</c> to a closing brace at the start of a line.
/// </summary>
public sealed partial record BicepResource(string Identifier, string Type, string ApiVersion, bool IsExisting, string Body)
{
    public static IEnumerable<BicepResource> Parse(string module)
    {
        foreach (Match match in Declaration().Matches(module))
        {
            yield return new BicepResource(
                match.Groups["id"].Value,
                match.Groups["type"].Value,
                match.Groups["version"].Value,
                match.Groups["existing"].Success,
                match.Groups["body"].Value);
        }
    }

    /// <summary>The value of the first property with this name, as written (<c>'text'</c>, <c>0</c>, <c>false</c>).</summary>
    public string? Property(string name)
    {
        var match = Regex.Match(Body, $@"^\s*{Regex.Escape(name)}: (?<value>.+?)\s*$", RegexOptions.Multiline);
        return match.Success ? match.Groups["value"].Value : null;
    }

    /// <summary>The text of a nested object property, from its opening to its matching closing brace.</summary>
    public string Block(string name)
    {
        var start = Regex.Match(Body, $@"^\s*{Regex.Escape(name)}: \{{\s*$", RegexOptions.Multiline);
        Assert.True(start.Success, $"'{Identifier}' has no '{name}' block.");
        var depth = 0;
        for (var i = start.Index + start.Length - 1; i < Body.Length; i++)
        {
            depth += Body[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return Body[start.Index..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced '{name}' block in '{Identifier}'.");
    }

    [GeneratedRegex(
        @"^resource (?<id>\w+) '(?<type>[^@']+)@(?<version>[^']+)' (?<existing>existing )?= \{\r?\n(?<body>.*?)^\}",
        RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Declaration();
}
