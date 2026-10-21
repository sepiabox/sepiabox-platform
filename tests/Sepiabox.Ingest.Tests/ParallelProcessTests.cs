using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Tests;

/// <summary>
/// <c>process --jobs</c> (D-078) with a stand-in for FFmpeg: videos never hold up photos, copies of the same bytes are
/// described in the order they were found, and photos too large to read ahead are streamed. No Docker or FFmpeg.
/// </summary>
public sealed class ParallelProcessTests
{
    [Fact]
    public async Task Finishes_the_photos_while_a_video_is_held_up_in_its_transcode()
    {
        using var drive = new TempFolder();
        using var work = new TempFolder();
        drive.Write("a1.mp4", Bytes(1));
        drive.Write("a2.mp4", Bytes(2));
        const int Photos = 6;
        for (var i = 0; i < Photos; i++)
        {
            // In a folder, which is read after the files beside it: the videos first.
            drive.Write($"photos/p{i}.jpg", TestPhotos.Taken($"2019:07:{i + 1:00} 10:00:00"));
        }

        var photoSeen = 0;
        using var gate = new ManualResetEventSlim(false);
        var photosDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new Counting(count =>
        {
            // Only photos can finish while the first video is held: the second waits for its turn to transcode.
            if (count == Photos)
            {
                photosDone.TrySetResult();
            }
        });

        var run = Task.Run(() => ProcessCommand.Run(
            Options(drive, work, jobs: 2), embedder: null, embedderWarning: null, filesDone: progress,
            hooks: new ProcessCommand.ProcessHooks(
                Video: new GatedVideo(gate),
                // The photos come after both videos are waiting: the first for the gate, the second for its turn.
                BeforeRead: path =>
                {
                    if (path.EndsWith(".jpg", StringComparison.Ordinal) && Interlocked.Increment(ref photoSeen) == 1)
                    {
                        Thread.Sleep(500);
                    }
                })));
        // Generous for a cold, shared CI runner (Windows took over 30 s once); a stall still never finishes before it.
        var first = await Task.WhenAny(photosDone.Task, Task.Delay(TimeSpan.FromMinutes(3)));
        gate.Set();
        var result = await run;

        Assert.Same(photosDone.Task, first);
        Assert.Equal(Photos + 2, result.Processed);
    }

    [Fact]
    public void Describes_copies_of_the_same_bytes_in_the_order_found_whatever_runs_first()
    {
        using var drive = new TempFolder();
        using var sequential = new TempFolder();
        using var parallel = new TempFolder();
        var photo = TestPhotos.Taken("2019:07:14 10:00:00");
        for (var i = 0; i < 50; i++)
        {
            drive.Write($"copy{i:00}.jpg", photo);
        }

        var changed = drive.Write("copy50.jpg", photo);
        // The first copy is slow, so without its turn every later copy would describe the photo before it.
        var hooks = new ProcessCommand.ProcessHooks(BeforeWork: path =>
        {
            if (path.EndsWith("copy00.jpg", StringComparison.Ordinal))
            {
                Thread.Sleep(300);
            }
        });

        Run(drive, sequential, 1, hooks);
        Run(drive, parallel, 8, hooks);
        Assert.Equal(Manifests(sequential), Manifests(parallel));

        // One copy changes: its old content loses it while the unchanged copies get their embedding in the same manifest.
        File.WriteAllBytes(changed, TestPhotos.Taken("2020:01:01 10:00:00"));
        File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddMinutes(1));
        Run(drive, sequential, 1, hooks, new FakeEmbedder());
        Run(drive, parallel, 8, hooks, new FakeEmbedder());
        var manifests = Manifests(parallel);
        Assert.Equal(Manifests(sequential), manifests);
        Assert.Equal(2, manifests.Count);
        Assert.Equal(50, manifests.Values.Max(m => ManifestJson.Deserialize(m).Sources.Count));
    }

    [Fact]
    public void Streams_photos_larger_than_the_read_ahead_and_describes_them_the_same()
    {
        using var drive = new TempFolder();
        using var buffered = new TempFolder();
        using var streamed = new TempFolder();
        for (var i = 0; i < 4; i++)
        {
            drive.Write($"p{i}.jpg", TestPhotos.Taken($"2019:07:{i + 1:00} 10:00:00"));
        }

        drive.Write("p0-copy.jpg", TestPhotos.Taken("2019:07:01 10:00:00"));

        var one = Run(drive, buffered, 4, new ProcessCommand.ProcessHooks());
        // A budget of one byte: every photo is larger, so none is read into memory.
        var other = Run(drive, streamed, 4, new ProcessCommand.ProcessHooks(ReadAheadBytes: 1));

        Assert.Equal(Manifests(buffered), Manifests(streamed));
        Assert.Equal((5, 0), (one.Processed, one.Problems.Count));
        Assert.Equal((5, 0), (other.Processed, other.Problems.Count));
    }

    private static ProcessResult Run(TempFolder drive, TempFolder work, int jobs, ProcessCommand.ProcessHooks hooks, FakeEmbedder? embedder = null) =>
        ProcessCommand.Run(Options(drive, work, jobs), embedder, embedderWarning: null, hooks: hooks);

    private static ProcessOptions Options(TempFolder drive, TempFolder work, int jobs) =>
        new(drive.Path, work.Path, "HD", ModelFolder: TestModels.None, Jobs: jobs, VideoJobs: 1);

    private static SortedDictionary<string, string> Manifests(TempFolder work) =>
        new(Directory.GetFiles(Path.Combine(work.Path, "manifests"), "*.json")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllText), StringComparer.Ordinal);

    private static byte[] Bytes(int seed)
    {
        var bytes = new byte[4096];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class Counting(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    // FFmpeg's stand-in: the first transcode waits for the gate, as a long video would.
    private sealed class GatedVideo(ManualResetEventSlim gate) : IVideoProcessor
    {
        private int _transcodes;

        public VideoProbe Probe(string path, CancellationToken cancellationToken = default) => new(640, 480, 1.0);

        public IReadOnlyList<ManifestDerivative> Generate(
            string sourcePath, string workFolder, string relativeFolder, VideoProbe probe, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _transcodes) == 1)
            {
                gate.Wait(TimeSpan.FromSeconds(60), cancellationToken);
            }

            return [];
        }
    }
}
