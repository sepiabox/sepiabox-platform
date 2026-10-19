using System.Security.Cryptography;

namespace Sepiabox.Pipeline.Models;

/// <summary>A downloaded model whose SHA-256 is not the pinned one, or a model with no SHA-256 pinned.</summary>
public sealed class ModelIntegrityException(string message) : Exception(message);

/// <summary>Downloads pinned models and refuses any file whose SHA-256 differs from <c>models.json</c> (D-016).</summary>
/// <param name="http">Its own timeout should be infinite: a 350 MB download is limited by <paramref name="inactivityTimeout"/> instead.</param>
/// <param name="inactivityTimeout">How long a read may wait for data before the download is abandoned; default one minute.</param>
public sealed class ModelDownloader(HttpClient http, TimeSpan? inactivityTimeout = null)
{
    private readonly TimeSpan _inactivityTimeout = inactivityTimeout ?? TimeSpan.FromMinutes(1);

    public enum Outcome
    {
        Downloaded,
        AlreadyPresent,
    }

    /// <summary>
    /// Makes sure <paramref name="model"/> is in <paramref name="folder"/> with the pinned SHA-256. The file is
    /// written under a temporary name and only moved into place once it has been verified, so a bad or
    /// interrupted download never replaces what was there or leaves a usable-looking model behind.
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
        DeleteAbandonedDownloads(target);

        // Always hashed here, never taken from the marker: this is the command that repairs a corrupted model.
        if (File.Exists(target) && ModelVerifier.Verify(model, target, trustMarker: false, out _, out _))
        {
            return Outcome.AlreadyPresent;
        }

        // Unique, so two downloads into the same folder never write the same file.
        var temporary = $"{target}.{Guid.NewGuid():N}.download";
        try
        {
            var actual = await DownloadAsync(model, temporary, bytesRead, cancellationToken);
            if (!string.Equals(actual, model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModelIntegrityException(
                    $"{model.Name} downloaded from {model.Url} has SHA-256 {actual}, but models.json pins {model.Sha256}. "
                    + "The file was discarded.");
            }

            File.Move(temporary, target, overwrite: true);
            ModelVerifier.Remember(model, target);
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

    /// <summary>
    /// Removes temporary files left by a download that was killed, once they are older than the inactivity
    /// timeout, so a download still running in another process keeps its file.
    /// </summary>
    private void DeleteAbandonedDownloads(string target)
    {
        var folder = Path.GetDirectoryName(target)!;
        foreach (var file in Directory.EnumerateFiles(folder, Path.GetFileName(target) + ".*.download"))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) <= _inactivityTimeout)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still open elsewhere; the next download tries again.
            }
        }
    }

    /// <summary>Writes the response to <paramref name="path"/> and returns its SHA-256.</summary>
    private async Task<string> DownloadAsync(ModelInfo model, string path, IProgress<long>? bytesRead, CancellationToken cancellationToken)
    {
        // Restarted before every read: a stalled connection fails after a minute without capping a slow download.
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            stalled.CancelAfter(_inactivityTimeout);
            using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, stalled.Token);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(stalled.Token);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20];
            long total = 0;
            while (true)
            {
                stalled.CancelAfter(_inactivityTimeout);
                var read = await body.ReadAsync(buffer, stalled.Token);
                // Only the wait for data counts: a slow disk or progress display is not a stalled connection.
                stalled.CancelAfter(Timeout.InfiniteTimeSpan);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                bytesRead?.Report(total);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (OperationCanceledException) when (stalled.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{model.Url} sent no data for {_inactivityTimeout.TotalSeconds:N0} seconds, so the download of {model.Name} was abandoned. "
                + "Run models download again to retry.");
        }
    }
}
