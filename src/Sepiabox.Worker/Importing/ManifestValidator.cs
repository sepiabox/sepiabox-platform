using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Worker.Importing;

/// <summary>Checks a manifest against <c>docs/schemas/manifest.v1.json</c> before anything is written.</summary>
public sealed class ManifestValidator
{
    private readonly JsonSchema _schema;

    public ManifestValidator()
    {
        using var stream = typeof(ManifestValidator).Assembly.GetManifestResourceStream("manifest.v1.json")
            ?? throw new InvalidOperationException("The manifest schema is not embedded in the worker.");
        using var reader = new StreamReader(stream);
        _schema = JsonSchema.FromText(reader.ReadToEnd());
    }

    /// <summary>Returns the manifest, or null with the reason it was rejected.</summary>
    public (Manifest? Manifest, string? Error) Validate(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return (null, $"not valid JSON: {ex.Message}");
        }

        var evaluation = _schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!evaluation.IsValid)
        {
            var errors = evaluation.Details
                .Where(detail => detail.Errors is { Count: > 0 })
                .SelectMany(detail => detail.Errors!.Values.Select(error => $"{detail.InstanceLocation}: {error}"));
            return (null, string.Join("; ", errors.DefaultIfEmpty("does not match the manifest schema")));
        }

        return (ManifestJson.Deserialize(json), null);
    }
}
