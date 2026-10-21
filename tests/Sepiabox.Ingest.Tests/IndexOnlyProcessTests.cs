using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Tests;

/// <summary>process --index-only (S-063, D-088), without FFmpeg: photos, the option, and the mode that belongs to the work folder.</summary>
public sealed class IndexOnlyProcessTests : IDisposable
{
    private const string WrongMode = "use a new --out for the other mode.";

    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void Reads_the_index_only_option_like_the_other_options()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work", "--index-only"], out var options, out _));
        Assert.True(options.IndexOnly);
        Assert.Equal(WorkFolderMode.IndexOnly, options.Mode);

        Assert.True(ProcessOptions.TryParse(["--index-only", "E:\\", "--faces", "--out", "work", "--retry-left-out", "--jobs", "2"], out options, out _));
        Assert.True(options.IndexOnly);
        Assert.True(options.Faces);
        Assert.True(options.RetryLeftOut);
        Assert.Equal(2, options.Jobs);
    }

    [Fact]
    public void Keeps_originals_unless_asked_otherwise()
    {
        Assert.True(ProcessOptions.TryParse(["E:\\", "--out", "work"], out var options, out _));
        Assert.False(options.IndexOnly);
        Assert.Equal(WorkFolderMode.KeepsOriginals, options.Mode);
    }

    [Fact]
    public void Makes_a_2048_preview_and_says_the_original_is_not_stored()
    {
        _drive.Write("big.jpg", TestPhotos.Sized(4000, 3000));

        var result = Process(indexOnly: true);

        Assert.Empty(result.Problems);
        var manifest = Assert.Single(ReadManifests());
        Assert.False(manifest.IsOriginalStored);
        Assert.Equal(
            [("thumb-256", 256, 192), ("preview-2048", 2048, 1536)],
            manifest.Derivatives.Select(d => (d.Name, d.Width, d.Height)));
        Assert.DoesNotContain(manifest.Derivatives, d => d.Name is "preview-1280" or "proxy-720p");
        foreach (var derivative in manifest.Derivatives)
        {
            Assert.True(File.Exists(Path.Combine(_work.Path, derivative.Path)));
        }

        // As the schema says: false written out, validated like any manifest.
        var json = File.ReadAllText(ManifestFile(manifest));
        Assert.False(JsonNode.Parse(json)!["originalStored"]!.GetValue<bool>());
        Assert.True(new ManifestValidator().TryValidate(json, out _, out var invalid), invalid);
    }

    [Fact]
    public void Does_not_upscale_a_small_photo()
    {
        _drive.Write("small.jpg", TestPhotos.Sized(1000, 750));

        Process(indexOnly: true);

        var preview = Assert.Single(Assert.Single(ReadManifests()).Derivatives, d => d.Name == "preview-2048");
        Assert.Equal((1000, 750), (preview.Width, preview.Height));
    }

    [Fact]
    public void Keeps_the_1280_preview_and_writes_no_original_stored_in_the_default_mode()
    {
        _drive.Write("big.jpg", TestPhotos.Sized(4000, 3000));

        Process(indexOnly: false);

        var manifest = Assert.Single(ReadManifests());
        Assert.Equal(["thumb-256", "preview-1280"], manifest.Derivatives.Select(d => d.Name));
        Assert.True(manifest.IsOriginalStored);
        Assert.False(JsonNode.Parse(File.ReadAllText(ManifestFile(manifest)))!.AsObject().ContainsKey("originalStored"));
    }

    [Fact]
    public void Records_the_mode_of_a_new_work_folder_on_its_first_run()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());

        Process(indexOnly: true);

        Assert.Equal(WorkFolderMode.IndexOnly, RecordedMode());
    }

    [Fact]
    public void Follows_the_recorded_mode_on_a_later_run_of_the_same_mode()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());
        Process(indexOnly: true);

        var second = Process(indexOnly: true);

        Assert.Equal((0, 1), (second.Processed, second.Skipped));
        Assert.Equal(WorkFolderMode.IndexOnly, RecordedMode());
    }

    [Theory]
    [InlineData(true, "This work folder is index-only; " + WrongMode)]
    [InlineData(false, "This work folder keeps originals; " + WrongMode)]
    public void Refuses_the_other_mode_before_reading_any_file_and_leaves_the_work_folder_as_it_was(bool firstIsIndexOnly, string message)
    {
        _drive.Write("a.jpg", TestPhotos.Porto());
        Process(indexOnly: firstIsIndexOnly);
        _drive.Write("b.jpg", TestPhotos.Porto("red"));
        var before = Snapshot(_work.Path);
        var read = new List<string>();
        var hooks = new ProcessCommand.ProcessHooks(BeforeRead: read.Add);

        var refused = Assert.Throws<WorkFolderModeException>(() => Run(Options(!firstIsIndexOnly), hooks: hooks));
        var refusedByCheck = Assert.Throws<WorkFolderModeException>(() => ProcessCommand.CheckMode(Options(!firstIsIndexOnly)));
        var refusedPublic = Assert.Throws<WorkFolderModeException>(() => ProcessCommand.Run(Options(!firstIsIndexOnly)));

        Assert.All(new[] { refused, refusedByCheck, refusedPublic }, e => Assert.Equal(message, e.Message));
        Assert.Empty(read);
        Assert.Equal(before, Snapshot(_work.Path));
        Assert.Equal(firstIsIndexOnly ? WorkFolderMode.IndexOnly : WorkFolderMode.KeepsOriginals, RecordedMode());
    }

    [Fact]
    public void A_work_folder_made_before_modes_were_recorded_keeps_originals_and_records_it_on_the_first_run()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());
        Process(indexOnly: false);
        MakeLegacy();
        _drive.Write("b.jpg", TestPhotos.Porto("red"));

        var result = Process(indexOnly: false);

        Assert.Equal((1, 1), (result.Processed, result.Skipped));
        Assert.Equal(WorkFolderMode.KeepsOriginals, RecordedMode());
        Assert.Equal(2, ReadManifests().Count);
        Assert.All(ReadManifests(), m => Assert.Equal(["thumb-256", "preview-1280"], m.Derivatives.Select(d => d.Name)));
    }

    [Fact]
    public void Index_only_on_a_work_folder_made_before_modes_were_recorded_is_refused_before_reading_any_file()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());
        Process(indexOnly: false);
        MakeLegacy();
        var before = Snapshot(_work.Path);
        var read = new List<string>();

        var refused = Assert.Throws<WorkFolderModeException>(() => Run(
            Options(indexOnly: true), hooks: new ProcessCommand.ProcessHooks(BeforeRead: read.Add)));

        Assert.Equal("This work folder keeps originals; " + WrongMode, refused.Message);
        Assert.Empty(read);
        Assert.Equal(before, Snapshot(_work.Path));
        Assert.Equal(WorkFolderMode.KeepsOriginals, RecordedMode());
    }

    [Fact]
    public void Finds_the_mode_in_a_write_ahead_log_an_interrupted_run_left_behind()
    {
        var database = Path.Combine(_work.Path, "state.db");
        // Open, so its -wal is not checkpointed into state.db: as after a run that was killed.
        using var running = new StateStore(database);
        running.RecordMode(WorkFolderMode.IndexOnly);
        Assert.True(File.Exists(database + "-wal"));

        var refused = Assert.Throws<WorkFolderModeException>(() => WorkFolderModes.Check(database, WorkFolderMode.KeepsOriginals));

        Assert.Equal("This work folder is index-only; " + WrongMode, refused.Message);
        WorkFolderModes.Check(database, WorkFolderMode.IndexOnly);
    }

    [Fact]
    public void Reads_the_embedding_and_the_faces_from_the_largest_preview_in_both_modes()
    {
        _drive.Write("big.jpg", TestPhotos.Sized(4000, 3000));
        var embedder = new FakeEmbedder();
        var faces = new FakeFaceDetector();

        var result = Run(Options(indexOnly: true, faces: true), embedder, faces);

        Assert.Empty(result.Problems);
        Assert.EndsWith("preview-2048.webp", Assert.Single(embedder.Embedded), StringComparison.Ordinal);
        Assert.EndsWith("preview-2048.webp", Assert.Single(faces.Detected), StringComparison.Ordinal);
        var manifest = Assert.Single(ReadManifests());
        Assert.Single(manifest.Embeddings);
        Assert.True(manifest.FacesProcessed);
        Assert.False(manifest.IsOriginalStored);
    }

    [Fact]
    public void Adds_the_embedding_to_an_unchanged_index_only_file_from_its_2048_preview()
    {
        _drive.Write("big.jpg", TestPhotos.Sized(3000, 2000));
        Run(Options(indexOnly: true));
        var embedder = new FakeEmbedder();

        var second = Run(Options(indexOnly: true), embedder);

        Assert.Equal((0, 1, 1), (second.Processed, second.Skipped, second.Embedded));
        Assert.EndsWith("preview-2048.webp", Assert.Single(embedder.Embedded), StringComparison.Ordinal);
    }

    [Fact]
    public void The_program_warns_once_at_the_start_of_an_index_only_run()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());

        var (exitCode, _, errors) = RunProgram("process", _drive.Path, "--out", _work.Path, "--index-only", "--ffmpeg", TestFfmpeg.None, "--models", TestModels.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, Occurrences(errors, ProcessCommand.IndexOnlyWarning));
        Assert.Equal("Index-only: the originals stay where they are and Sepiabox keeps no copy of them.", ProcessCommand.IndexOnlyWarning);

        var (_, _, again) = RunProgram("process", _drive.Path, "--out", _work.Path, "--index-only", "--ffmpeg", TestFfmpeg.None, "--models", TestModels.None);
        Assert.Equal(1, Occurrences(again, ProcessCommand.IndexOnlyWarning));
    }

    [Fact]
    public void The_program_does_not_warn_in_the_default_mode()
    {
        _drive.Write("a.jpg", TestPhotos.Porto());

        var (exitCode, _, errors) = RunProgram("process", _drive.Path, "--out", _work.Path, "--ffmpeg", TestFfmpeg.None, "--models", TestModels.None);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Index-only", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_program_exits_with_the_usage_code_and_the_message_when_the_other_mode_is_asked_for(bool firstIsIndexOnly)
    {
        _drive.Write("a.jpg", TestPhotos.Porto());
        string[] common = ["--out", _work.Path, "--ffmpeg", TestFfmpeg.None, "--models", TestModels.None];
        Assert.Equal(0, RunProgram(["process", _drive.Path, .. common, .. firstIsIndexOnly ? new[] { "--index-only" } : []]).ExitCode);
        var before = Snapshot(_work.Path);

        var (exitCode, output, errors) = RunProgram(["process", _drive.Path, .. common, .. firstIsIndexOnly ? [] : new[] { "--index-only" }]);

        Assert.Equal(2, exitCode);
        Assert.Contains($"This work folder {(firstIsIndexOnly ? "is index-only" : "keeps originals")}; use a new --out for the other mode.", errors, StringComparison.Ordinal);
        // Stopped at once: no warning, no summary of a run.
        Assert.DoesNotContain(ProcessCommand.IndexOnlyWarning, errors, StringComparison.Ordinal);
        Assert.DoesNotContain("Processed", output, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(_work.Path));
    }

    private static int Occurrences(string text, string value) =>
        text.Split(value).Length - 1;

    // The built sepiabox-ingest next to the tests, run as a user runs it.
    private static (int ExitCode, string Output, string Errors) RunProgram(params string[] args)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "sepiabox-ingest.dll");
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(dll);
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(info)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, errors.GetAwaiter().GetResult());
    }

    private ProcessResult Process(bool indexOnly) => Run(Options(indexOnly));

    private static ProcessResult Run(
        ProcessOptions options, Sepiabox.Pipeline.Embeddings.IImageEmbedder? embedder = null, Sepiabox.Pipeline.Faces.IFaceDetector? faces = null,
        ProcessCommand.ProcessHooks? hooks = null) =>
        ProcessCommand.Run(options, embedder: embedder, embedderWarning: null, faces: faces, hooks: hooks);

    private ProcessOptions Options(bool indexOnly, bool faces = false) =>
        new(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None, Faces: faces, IndexOnly: indexOnly);

    private List<Manifest> ReadManifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];

    private string ManifestFile(Manifest manifest) => Path.Combine(_work.Path, "manifests", manifest.Id + ".json");

    private WorkFolderMode RecordedMode()
    {
        using var state = new StateStore(Path.Combine(_work.Path, "state.db"));
        return state.Mode;
    }

    // What an earlier build left: the files table and no mode.
    private void MakeLegacy()
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_work.Path, "state.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE meta";
        command.ExecuteNonQuery();
    }

    // Every file of the folder and its content: what "unchanged" means.
    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(folder, file),
            file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
}

/// <summary>The address SQLite opens the mode check's read-only connection with (S-063).</summary>
public sealed class WorkFolderModeUriTests
{
    [Theory]
    [InlineData("/home/ana/work/state.db", true, "file:///home/ana/work/state.db?mode=ro&immutable=1")]
    [InlineData("/home/ana/work/state.db", false, "file:///home/ana/work/state.db?mode=ro")]
    [InlineData(@"C:\Users\Ana\work\state.db", true, "file:///C:/Users/Ana/work/state.db?mode=ro&immutable=1")]
    [InlineData(@"\\nas\share\work\state.db", true, "file:////nas/share/work/state.db?mode=ro&immutable=1")]
    [InlineData(@"\\nas\share\work\state.db", false, "file:////nas/share/work/state.db?mode=ro")]
    [InlineData("/home/ana/fotos 2019/a#b/50%/c?d/são josé/state.db", true, "file:///home/ana/fotos%202019/a%23b/50%25/c%3Fd/s%C3%A3o%20jos%C3%A9/state.db?mode=ro&immutable=1")]
    [InlineData(@"D:\Álbum #1\state.db", false, "file:///D:/%C3%81lbum%20%231/state.db?mode=ro")]
    public void Builds_the_address_for_a_unix_path_a_drive_a_network_share_and_awkward_names(string path, bool immutable, string expected)
    {
        Assert.Equal(expected, WorkFolderModes.ReadOnlyUri(path, immutable));
    }
}
