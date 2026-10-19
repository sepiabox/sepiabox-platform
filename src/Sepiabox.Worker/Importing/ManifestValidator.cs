using System.Diagnostics.CodeAnalysis;
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

    /// <summary>Reads a manifest, or explains why it was rejected.</summary>
    public bool TryValidate(string json, [NotNullWhen(true)] out Manifest? manifest, [NotNullWhen(false)] out string? error)
    {
        manifest = null;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"not valid JSON: {ex.Message}";
            return false;
        }

        // Formats (date-time) are only annotations unless required.
        var evaluation = _schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        if (!evaluation.IsValid)
        {
            var errors = evaluation.Details
                .Where(detail => detail.Errors is { Count: > 0 })
                .SelectMany(detail => detail.Errors!.Values.Select(message => $"{detail.InstanceLocation}: {message}"));
            error = string.Join("; ", errors.DefaultIfEmpty("does not match the manifest schema"));
            return false;
        }

        // The schema cannot express everything the reader needs (an impossible date, an integer that
        // does not fit in an int, 1.0 for an integer), so a schema-valid manifest can still fail here.
        try
        {
            manifest = ManifestJson.Deserialize(json);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or NotSupportedException)
        {
            error = $"cannot be read: {ex.Message}";
            return false;
        }

        error = null;
        return true;
    }
}
