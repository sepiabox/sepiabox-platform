using System.Security.Cryptography;

namespace Sepiabox.Pipeline.Models;

/// <summary>A downloaded model whose SHA-256 is not the pinned one, or a model with no SHA-256 pinned.</summary>
public sealed class ModelIntegrityException(string message) : Exception(message);

/// <summary>Downloads pinned models and refuses any file whose SHA-256 differs from <c>models.json</c> (D-016).</summary>
public sealed class ModelDownloader(HttpClient http)
{
    public enum Outcome
    {
        Downloaded,
        AlreadyPresent,
    }

    /// <summary>
    /// Makes sure <paramref name="model"/> is in <paramref name="folder"/> with the pinned SHA-256. The file is
    /// written next to its final name and only moved into place once it has been verified, so a bad or
    /// interrupted download never leaves a usable-looking model behind.
    /// </summary>
    public async Task<Outcome> EnsureAsync(
        ModelInfo model, string folder, IProgress<long>? bytesRead = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Sha256))
        {
            throw new ModelIntegrityException($"No SHA-256 is pinned for {model.Name} in models.json, so it cannot be verified.");
        }

        Directory.CreateDirectory(folder);
        var target = ModelFolder.PathOf(folder, model);
        if (File.Exists(target) && string.Equals(await HashFileAsync(target, cancellationToken), model.Sha256, StringComparison.Ordinal))
        {
            return Outcome.AlreadyPresent;
        }

        var temporary = target + ".download";
        try
        {
            string actual;
            using (var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1 << 20];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                    bytesRead?.Report(total);
                }

                actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            if (!string.Equals(actual, model.Sha256, StringComparison.Ordinal))
            {
                throw new ModelIntegrityException(
                    $"{model.Name} downloaded from {model.Url} has SHA-256 {actual}, but models.json pins {model.Sha256}. "
                    + "The file was discarded.");
            }

            File.Move(temporary, target, overwrite: true);
            return Outcome.Downloaded;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
    }
}
