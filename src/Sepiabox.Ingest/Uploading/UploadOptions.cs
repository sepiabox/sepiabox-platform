using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Ingest.Uploading;

public enum UploadDestination
{
    /// <summary>Azurite, or the storage in <c>SEPIABOX_BLOB_CONNECTION</c>.</summary>
    Local,

    /// <summary>An Azure storage account, signed in with <c>az login</c>.</summary>
    Azure,
}

/// <param name="WorkFolder">Work folder written by <c>process</c>.</param>
/// <param name="Destination">Where the blobs go.</param>
/// <param name="Account">Storage account name; required for <see cref="UploadDestination.Azure"/>.</param>
public sealed record UploadOptions(string WorkFolder, UploadDestination Destination, string? Account = null)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out UploadOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        string? workFolder = null;
        UploadDestination? destination = null;
        string? account = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--destination" or "--account" when i + 1 >= args.Count:
                    error = $"{args[i]} needs a value.";
                    return false;

                case "--destination":
                    destination = args[++i] switch
                    {
                        "local" => UploadDestination.Local,
                        "azure" => UploadDestination.Azure,
                        _ => null,
                    };
                    if (destination is null)
                    {
                        error = $"Unknown destination '{args[i]}'. Use local or azure.";
                        return false;
                    }

                    break;

                case "--account":
                    account = args[++i];
                    break;

                case var arg when arg.StartsWith('-'):
                    error = $"Unknown option '{arg}'.";
                    return false;

                case var arg when workFolder is null:
                    workFolder = arg;
                    break;

                default:
                    error = "Only one work folder can be uploaded at a time.";
                    return false;
            }
        }

        if (workFolder is null)
        {
            error = "Tell upload which work folder to send: the one given to process --out.";
            return false;
        }

        if (destination is null)
        {
            error = "Tell upload where to send the files with --destination local or --destination azure.";
            return false;
        }

        if (destination == UploadDestination.Azure && string.IsNullOrWhiteSpace(account))
        {
            error = "--destination azure needs the storage account name with --account <name>.";
            return false;
        }

        options = new UploadOptions(workFolder, destination.Value, account?.Trim());
        error = null;
        return true;
    }
}
