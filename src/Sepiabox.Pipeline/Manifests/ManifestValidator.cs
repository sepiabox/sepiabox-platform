using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Sepiabox.Pipeline.Manifests;

/// <summary>
/// Checks a manifest against <c>docs/schemas/manifest.v1.json</c> before anything is written. Used by every
/// reader of a manifest that someone else wrote: the worker's import and <c>upload</c>'s merge.
/// </summary>
public sealed class ManifestValidator
{
    private readonly JsonSchema _schema;

    public ManifestValidator()
    {
        using var stream = typeof(ManifestValidator).Assembly.GetManifestResourceStream("manifest.v1.json")
            ?? throw new InvalidOperationException("The manifest schema is not embedded in Sepiabox.Pipeline.");
        using var reader = new StreamReader(stream);
        _schema = JsonSchema.FromText(reader.ReadToEnd());
    }

    /// <summary>Reads a manifest, or explains why it was rejected. Never throws for bad input.</summary>
    public bool TryValidate(string json, [NotNullWhen(true)] out Manifest? manifest, [NotNullWhen(false)] out string? error)
    {
        manifest = null;
        try
        {
            var node = JsonNode.Parse(json);

            // Format validation is required, so "format" keywords are rules, not annotations. The schema
            // has one: date-time on source times. capturedAt uses a local-time pattern instead (D-037).
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
            manifest = ManifestJson.Deserialize(json);
        }
        catch (JsonException ex)
        {
            error = $"not valid JSON: {ex.Message}";
            return false;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or NotSupportedException
            or ArgumentException or InvalidOperationException)
        {
            // ArgumentException: a property repeated in the same object.
            error = $"cannot be read: {ex.Message}";
            return false;
        }

        if (manifest is null)
        {
            error = "is empty";
            return false;
        }

        error = null;
        return true;
    }
}
