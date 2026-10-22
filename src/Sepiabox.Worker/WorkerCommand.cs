using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Worker;

public enum WorkerAction
{
    Migrate,
    ImportOnce,
    ImportFromQueue,
    ProcessFromQueue,
    GeoNamesLoad,
    GeoNamesDownload,
    GeoNamesUpdate,
    PlacesRefresh,
    FacesSweep,
    EventsRebuild,
    EventsClassify,
    SyncGoogleDrive,
    SyncOneDrive,
    SyncAll,
    AssetsDelete,
}

/// <summary>The three GeoNames dump files that <c>geonames load</c> reads (S-012).</summary>
public sealed record GeoNamesSource(string Cities, string Admin1, string Countries);

/// <param name="Action">What to run.</param>
/// <param name="MigrateFirst">Apply pending migrations before importing (D-013).</param>
/// <param name="GeoNames">The files to load, for <see cref="WorkerAction.GeoNamesLoad"/> only.</param>
/// <param name="Folder">Where <see cref="WorkerAction.GeoNamesDownload"/> writes the files.</param>
/// <param name="Rescan">The connected account (its address) whose sync state is forgotten first, for <see cref="WorkerAction.SyncGoogleDrive"/> or <see cref="WorkerAction.SyncOneDrive"/> only (S-038, S-039).</param>
/// <param name="AllPlaces">Name every asset's place again, for <see cref="WorkerAction.PlacesRefresh"/> only.</param>
/// <param name="Source">The source whose assets <see cref="WorkerAction.AssetsDelete"/> deletes.</param>
/// <param name="Yes">Really delete (<c>--yes</c>); without it <c>assets delete</c> is a dry run.</param>
/// <param name="Expect">How many assets must match for <c>--yes</c> to delete (<c>--expect</c>).</param>
public sealed record WorkerCommand(
    WorkerAction Action, bool MigrateFirst, GeoNamesSource? GeoNames = null, bool AllPlaces = false, string? Folder = null,
    string? Source = null, bool Yes = false, int? Expect = null, string? Rescan = null)
{
    public const string Usage = """
        Usage: sepiabox-worker <command>

        Commands:
          migrate                    Apply the catalog's database migrations.
          import --once [--migrate]  Import manifests that are new or changed since the last import,
                                     then exit. --migrate applies migrations first.
          import --from-queue        Import the manifests named by the storage events waiting in the
                                     queue (ServiceBus:Namespace, ServiceBus:Queue), then exit when
                                     it is empty. Never migrates (D-063).
          process --from-queue       Process the files that arrived in the incoming container from a cloud
                                     source (S-036): hash, metadata, thumbnails or video versions, CLIP and,
                                     when face recognition is on, faces; write the derivatives and the
                                     manifest; move the original to originals (custody mode, Cold) or leave
                                     it at its source (reference mode); delete the incoming blob. Takes the
                                     storage events from the queue (ServiceBus:Namespace, ServiceBus:Queue),
                                     then exits when it is empty. Needs FFmpeg and the models folder
                                     (Search:ModelFolder); never migrates.
          geonames load <cities500.zip|txt> --admin1 <admin1CodesASCII.txt> --countries <countryInfo.txt>
                                     Replace the GeoNames cities with these dump files
                                     (https://download.geonames.org/export/dump/, CC BY 4.0).
          geonames download <folder> Download those three files from GeoNames into an existing folder,
                                     logging each file's SHA-256, size and date.
          geonames update            Download the files into a temporary folder, load them unless they
                                     have fewer than 150,000 cities or 10% fewer than loaded now, then
                                     run places refresh. What the sepiabox-geonames job runs (S-059).
          places refresh [--all]     Name the place of assets that have a location but no place yet;
                                     --all names every asset with a location again (after a newer dump).
          sync [google-drive|onedrive [--rescan <account address>]]
                                     For each connected Google Drive (S-037, S-038) and OneDrive (S-039), put the
                                     photos and videos that are new or changed in the incoming container in
                                     reference mode, and mark the sources of those that are gone; the first run
                                     lists everything, Sync:MaxFilesPerRun (default 500) files a run. Reads the
                                     refresh tokens with the web's key ring and each service's OAuth client
                                     (Auth:Google, OneDrive) from Key Vault. Without a service name, both: what the
                                     sepiabox-sync job runs, daily; never migrates.
                                     --rescan first forgets what the sync did for that account (of that one service) (its cursor and the
                                     files it staged), so the whole Drive is staged again, for files the processing job skipped.
          faces sweep                After face recognition was turned off, erase the faces found before
                                     that from every manifest written since the last sweep (S-022); does
                                     nothing while it was never turned off. What the sepiabox-faces job runs.
          events rebuild             Group every dated asset into occasions (S-023): a new one starts after
                                     a gap of more than 6 hours or 50 km. Safe to run again: with the same
                                     assets it changes nothing. Run it after importing.
          events classify            Give a type (wedding, birthday, beach, christmas, graduation, snow, party,
                                     trip) to every occasion that has none (S-024), from the CLIP embeddings
                                     of its photos and, for a trip, its distance from the home place. Needs
                                     the CLIP text model: sepiabox-ingest models download (or Search:ModelFolder).
                                     Safe to run again. Run it after events rebuild.
          assets delete --source <name> [--yes --expect <n>]
                                     Remove the assets that came only from the source <name> (S-057): without
                                     --yes a dry run that lists every matched asset with its source paths and
                                     blobs and the totals; with --yes --expect <n> it deletes, and refuses to
                                     start unless exactly n assets match. An asset any other source also holds
                                     is kept. Exit 0 done, 1 some assets kept or failed (listed), 2 usage.
                                     What the manual sepiabox-admin job runs.
        """;

    public static bool TryParse(string[] args, [NotNullWhen(true)] out WorkerCommand? command, out string error)
    {
        command = null;
        error = string.Empty;

        switch (args)
        {
            case ["migrate"]:
                command = new WorkerCommand(WorkerAction.Migrate, false);
                return true;
            case ["import", "--from-queue"]:
                command = new WorkerCommand(WorkerAction.ImportFromQueue, false);
                return true;
            case ["process", "--from-queue"]:
                command = new WorkerCommand(WorkerAction.ProcessFromQueue, false);
                return true;
            case ["import", .. var options] when options.All(o => o is "--once" or "--migrate"):
                if (!options.Contains("--once"))
                {
                    error = "import needs --once or --from-queue.";
                    return false;
                }

                command = new WorkerCommand(WorkerAction.ImportOnce, options.Contains("--migrate"));
                return true;
            case ["geonames", "load", var cities, .. var options] when !cities.StartsWith("--", StringComparison.Ordinal):
                return TryParseGeoNamesLoad(cities, options, out command, out error);
            case ["geonames", "download", var folder] when !folder.StartsWith("--", StringComparison.Ordinal):
                command = new WorkerCommand(WorkerAction.GeoNamesDownload, false, Folder: folder);
                return true;
            case ["geonames", "update"]:
                command = new WorkerCommand(WorkerAction.GeoNamesUpdate, false);
                return true;
            case ["places", "refresh"]:
                command = new WorkerCommand(WorkerAction.PlacesRefresh, false);
                return true;
            case ["places", "refresh", "--all"]:
                command = new WorkerCommand(WorkerAction.PlacesRefresh, false, AllPlaces: true);
                return true;
            case ["faces", "sweep"]:
                command = new WorkerCommand(WorkerAction.FacesSweep, false);
                return true;
            case ["sync", "google-drive"]:
                command = new WorkerCommand(WorkerAction.SyncGoogleDrive, false);
                return true;
            case ["sync", "google-drive", "--rescan", var rescan] when !rescan.StartsWith("--", StringComparison.Ordinal) && rescan.Contains('@', StringComparison.Ordinal):
                command = new WorkerCommand(WorkerAction.SyncGoogleDrive, false, Rescan: rescan);
                return true;
            case ["sync", "onedrive", "--rescan", var rescanOneDrive] when !rescanOneDrive.StartsWith("--", StringComparison.Ordinal) && rescanOneDrive.Contains('@', StringComparison.Ordinal):
                command = new WorkerCommand(WorkerAction.SyncOneDrive, false, Rescan: rescanOneDrive);
                return true;
            case ["sync", "onedrive"]:
                command = new WorkerCommand(WorkerAction.SyncOneDrive, false);
                return true;
            case ["sync"]:
                command = new WorkerCommand(WorkerAction.SyncAll, false);
                return true;
            case ["events", "rebuild"]:
                command = new WorkerCommand(WorkerAction.EventsRebuild, false);
                return true;
            case ["events", "classify"]:
                command = new WorkerCommand(WorkerAction.EventsClassify, false);
                return true;
            case ["assets", "delete", .. var options]:
                return TryParseAssetsDelete(options, out command, out error);
            case [_, ..]:
                error = $"Unknown command or option in '{string.Join(' ', args)}'.";
                return false;
            default:
                error = "Missing command.";
                return false;
        }
    }

    private static bool TryParseGeoNamesLoad(
        string cities, string[] options, [NotNullWhen(true)] out WorkerCommand? command, out string error)
    {
        command = null;
        error = string.Empty;
        string? admin1 = null;
        string? countries = null;
        for (var i = 0; i < options.Length; i += 2)
        {
            if (i + 1 >= options.Length || options[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{options[i]} needs a file.";
                return false;
            }

            switch (options[i])
            {
                case "--admin1" when admin1 is null:
                    admin1 = options[i + 1];
                    break;
                case "--countries" when countries is null:
                    countries = options[i + 1];
                    break;
                default:
                    error = $"Unknown or repeated option '{options[i]}' for geonames load.";
                    return false;
            }
        }

        if (admin1 is null || countries is null)
        {
            error = "geonames load needs --admin1 and --countries.";
            return false;
        }

        command = new WorkerCommand(WorkerAction.GeoNamesLoad, false, new GeoNamesSource(cities, admin1, countries));
        return true;
    }

    private static bool TryParseAssetsDelete(string[] options, [NotNullWhen(true)] out WorkerCommand? command, out string error)
    {
        command = null;
        error = string.Empty;
        string? source = null;
        var yes = false;
        int? expect = null;
        for (var i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--source" when source is null && i + 1 < options.Length && !options[i + 1].StartsWith("--", StringComparison.Ordinal):
                    source = options[++i];
                    break;
                case "--source":
                    error = "--source needs the name of one source, once.";
                    return false;
                case "--yes":
                    yes = true;
                    break;
                case "--expect" when expect is null && i + 1 < options.Length
                    && int.TryParse(options[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count):
                    expect = count;
                    i++;
                    break;
                case "--expect":
                    error = "--expect needs a whole number, once.";
                    return false;
                default:
                    error = $"Unknown option '{options[i]}' for assets delete.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            error = "assets delete needs --source <name>.";
            return false;
        }

        if (yes != (expect is not null))
        {
            error = "--yes and --expect <n> go together: without them assets delete only lists what it would delete.";
            return false;
        }

        command = new WorkerCommand(WorkerAction.AssetsDelete, false, Source: source.Trim(), Yes: yes, Expect: expect);
        return true;
    }
}
