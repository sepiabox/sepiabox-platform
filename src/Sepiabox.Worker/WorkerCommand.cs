using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Worker;

public enum WorkerAction
{
    Migrate,
    ImportOnce,
    ImportFromQueue,
    Export,
    GeoNamesLoad,
    GeoNamesDownload,
    GeoNamesUpdate,
    PlacesRefresh,
    FacesSweep,
    EventsRebuild,
    EventsClassify,
}

/// <summary>The three GeoNames dump files that <c>geonames load</c> reads (S-012).</summary>
public sealed record GeoNamesSource(string Cities, string Admin1, string Countries);

/// <param name="Action">What to run.</param>
/// <param name="MigrateFirst">Apply pending migrations before importing (D-013).</param>
/// <param name="GeoNames">The files to load, for <see cref="WorkerAction.GeoNamesLoad"/> only.</param>
/// <param name="Folder">Where <see cref="WorkerAction.GeoNamesDownload"/> writes the files.</param>
/// <param name="AllPlaces">Name every asset's place again, for <see cref="WorkerAction.PlacesRefresh"/> only.</param>
public sealed record WorkerCommand(WorkerAction Action, bool MigrateFirst, GeoNamesSource? GeoNames = null, bool AllPlaces = false, string? Folder = null)
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
          export                     Write the zip of each pending export request (S-040): catalog.json,
                                     originals.txt with 24-hour links, and README.txt, to the exports
                                     container; face data only when the request asked. Does nothing when no
                                     one has asked. What the sepiabox-export job runs, every few minutes.
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
            case ["export"]:
                command = new WorkerCommand(WorkerAction.Export, false);
                return true;
            case ["import", "--from-queue"]:
                command = new WorkerCommand(WorkerAction.ImportFromQueue, false);
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
            case ["events", "rebuild"]:
                command = new WorkerCommand(WorkerAction.EventsRebuild, false);
                return true;
            case ["events", "classify"]:
                command = new WorkerCommand(WorkerAction.EventsClassify, false);
                return true;
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
}
