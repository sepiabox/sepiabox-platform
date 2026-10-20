using Microsoft.ML.OnnxRuntime;

namespace Sepiabox.Pipeline.Models;

/// <summary>An input the caller feeds, with its exact shape when the caller depends on one.</summary>
public sealed record ModelInput(string Name, IReadOnlyList<int>? Shape = null);

/// <summary>
/// Opens a pinned model with ONNX Runtime (D-044): the file must exist, match its SHA-256 and have the outputs the
/// caller reads and the inputs it feeds. Anything else is a warning, never an exception, so a run goes on without what the model computes.
/// </summary>
public static class ModelSession
{
    private const string Forgotten = "Its verification was removed, so the next run checks its SHA-256 again.";

    /// <param name="modelFolder">Where <c>models download</c> put the models.</param>
    /// <param name="model">The pinned model.</param>
    /// <param name="what">How the warning names the model, such as "The CLIP model".</param>
    /// <param name="consequence">What the run goes without, such as "so no embeddings were computed".</param>
    /// <param name="remedy">What the user should do, which names <c>sepiabox-ingest models download</c>.</param>
    /// <param name="inputs">Inputs the caller feeds; a model without one of them, or with another shape, is refused.</param>
    /// <param name="outputs">Outputs the caller reads; a model without one of them is refused.</param>
    /// <param name="warning">Why no session was opened.</param>
    public static InferenceSession? TryOpen(
        string modelFolder,
        ModelInfo model,
        string what,
        string consequence,
        string remedy,
        IReadOnlyCollection<ModelInput> inputs,
        IReadOnlyCollection<string> outputs,
        out string? warning)
    {
        var path = ModelFolder.PathOf(modelFolder, model);
        if (!File.Exists(path))
        {
            warning = $"{what} is not in {modelFolder}, {consequence}. {remedy}";
            return null;
        }

        try
        {
            if (!ModelVerifier.Verify(model, path, trustMarker: true, out var actual, out _))
            {
                warning = $"{path} has SHA-256 {actual}, not the {model.Sha256} pinned in models.json, {consequence}. {remedy}";
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = $"Could not read {path} ({ex.Message}), {consequence}. {remedy}";
            return null;
        }

        InferenceSession session;
        try
        {
            // Errors only: the SFace export lists its weights as graph inputs, and ONNX Runtime warns about each one
            // on every run, which would bury the run's own output.
            using var options = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            session = new InferenceSession(path, options);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            // ONNX Runtime's native library is missing or broken on this computer: the model itself may be fine.
            warning = $"ONNX Runtime could not start ({ex.Message}), {consequence}. Reinstall sepiabox-ingest; the model files are kept.";
            return null;
        }
        catch (OnnxRuntimeException ex)
        {
            // Matching the pin but unusable: whatever the marker said, the next run checks the file again.
            ModelVerifier.Forget(path);
            warning = $"ONNX Runtime could not load {path} ({ex.Message}), {consequence}. {Forgotten} {remedy}";
            return null;
        }

        var badInput = inputs.FirstOrDefault(input => !session.InputMetadata.TryGetValue(input.Name, out var metadata)
            || (input.Shape is { } shape && !metadata.Dimensions.SequenceEqual(shape)));
        if (badInput is not null)
        {
            var found = string.Join(", ", session.InputMetadata.Select(i => $"{i.Key} [{string.Join("×", i.Value.Dimensions)}]"));
            var expected = badInput.Shape is null ? badInput.Name : $"{badInput.Name} [{string.Join("×", badInput.Shape)}]";
            session.Dispose();
            ModelVerifier.Forget(path);
            warning = $"{path} has no input {expected} (it has {found}), {consequence}. {Forgotten} {remedy}";
            return null;
        }

        var missing = outputs.FirstOrDefault(output => !session.OutputMetadata.ContainsKey(output));
        if (missing is not null)
        {
            var found = string.Join(", ", session.OutputMetadata.Keys);
            session.Dispose();
            ModelVerifier.Forget(path);
            warning = $"{path} has no {missing} output (it has {found}), {consequence}. {Forgotten} {remedy}";
            return null;
        }

        warning = null;
        return session;
    }
}
