using ImageMagick;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Pipeline.Processing;

/// <summary>How making a file's derivatives ended (S-065).</summary>
public enum DerivativeStatus
{
    /// <summary>Made (or, for a photo, thumbnails; for a video, poster and version).</summary>
    Done,

    /// <summary>A video, and no video processor to make its poster and version.</summary>
    Pending,

    /// <summary>A file with a video extension that holds no video (a tracker module, a stream-less container, D-045).</summary>
    NotVideo,

    /// <summary>A photo whose content cannot be decoded (D-105).</summary>
    Damaged,

    /// <summary>Could not be made; <see cref="DerivativeResult.Problem"/> says why.</summary>
    Failed,
}

/// <param name="Manifest">The manifest with the derivatives (and, for a video, its probed size, duration and place).</param>
/// <param name="Status">How it ended.</param>
/// <param name="NoVideo">For <see cref="DerivativeStatus.NotVideo"/>, why.</param>
/// <param name="Problem">For <see cref="DerivativeStatus.Damaged"/> and <see cref="DerivativeStatus.Failed"/>, the error's own message.</param>
/// <param name="WithoutSound">When a video's version was made without sound, why (D-084).</param>
/// <param name="Sound">What a video's version has for sound.</param>
public sealed record DerivativeResult(
    Manifest Manifest, DerivativeStatus Status, NoVideoReason? NoVideo = null, string? Problem = null, string? WithoutSound = null, VideoSound? Sound = null);

/// <summary>
/// Makes a file's derivatives for a mode (S-065, D-118): a photo's thumbnails (the larger index-only sizes when the original is
/// not kept, D-088), a video's poster and version through the processor it is given. The one copy of the rules <c>process</c>
/// and the cloud worker share: a tracker module is not a video, a file with no video stream is left out, a damaged photo is told
/// from one that could not be made. What each does with the result (a state record, a left-out blob) stays with the caller.
/// </summary>
public static class DerivativeMaker
{
    /// <summary>
    /// Makes the derivatives of <paramref name="manifest"/> from <paramref name="sourcePath"/> (or from
    /// <paramref name="content"/>, a photo already read into memory), written under <paramref name="outputRoot"/>.
    /// </summary>
    public static DerivativeResult Make(
        Manifest manifest, string sourcePath, byte[]? content, string outputRoot, bool indexOnly, IVideoProcessor? video,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var folder = $"derivatives/{manifest.Id}";
        try
        {
            if (manifest.Kind == MediaKind.Photo)
            {
                IReadOnlyList<ManifestDerivative> thumbnails;
                var sizes = indexOnly ? ThumbnailGenerator.IndexOnlyPhotoSizes : ThumbnailGenerator.PhotoSizes;
                if (content is null)
                {
                    thumbnails = ThumbnailGenerator.Generate(sourcePath, outputRoot, folder, sizes);
                }
                else
                {
                    using var stream = new MemoryStream(content, writable: false);
                    thumbnails = ThumbnailGenerator.Generate(stream, outputRoot, folder, sizes);
                }

                return new DerivativeResult(manifest with { Derivatives = thumbnails }, DerivativeStatus.Done);
            }

            // Checked without FFmpeg, whose module support depends on how it was built.
            if (TrackerModule.IsTrackerModule(sourcePath))
            {
                return new DerivativeResult(manifest, DerivativeStatus.NotVideo, NoVideoReason.TrackerModule);
            }

            if (video is null)
            {
                return new DerivativeResult(manifest, DerivativeStatus.Pending);
            }

            var probe = video.Probe(sourcePath, cancellationToken);
            var generated = video.Generate(sourcePath, outputRoot, folder, probe, cancellationToken);
            return new DerivativeResult(
                manifest with
                {
                    Width = probe.Width,
                    Height = probe.Height,
                    DurationSeconds = probe.DurationSeconds ?? manifest.DurationSeconds,
                    // MetadataExtractor misses some containers' positions (an MP4's loci box), which ffprobe reads.
                    Location = manifest.Location ?? probe.Location,
                    Derivatives = generated.Derivatives,
                },
                DerivativeStatus.Done,
                WithoutSound: generated.WithoutSoundBecause,
                Sound: generated.Sound);
        }
        catch (NoVideoStreamException ex)
        {
            return new DerivativeResult(manifest, DerivativeStatus.NotVideo, ex.Reason);
        }
        catch (MagickException ex) when (manifest.Kind == MediaKind.Photo && IsDamage(ex))
        {
            return new DerivativeResult(manifest, DerivativeStatus.Damaged, Problem: ex.Message);
        }
        catch (Exception ex) when (ex is MagickException or VideoProcessingException)
        {
            return new DerivativeResult(manifest, DerivativeStatus.Failed, Problem: ex.Message);
        }
    }

    // Magick's errors that say the file's own content is bad: corrupt data, and a codec that read it and refused it (a
    // JPEG or TIFF marker that makes no sense is a coder error, as in a corrupt Canon CR2). Not a missing codec, a delegate
    // that failed or a blob that could not be read: those can come from the machine or a drive that dropped out, would
    // mark good files, and the sticky status would hide the cause. They stay failures, tried again on the next run.
    public static bool IsDamage(MagickException ex) => ex is
        MagickCorruptImageErrorException or MagickCorruptImageWarningException or MagickCoderErrorException;
}
