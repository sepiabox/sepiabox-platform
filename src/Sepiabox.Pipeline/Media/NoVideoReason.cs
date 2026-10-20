namespace Sepiabox.Pipeline.Media;

/// <summary>Why a file with a video extension is left out as holding no video (D-045).</summary>
public enum NoVideoReason
{
    /// <summary>A tracker-music module, recognised by its ProTracker-family tag.</summary>
    TrackerModule,

    /// <summary>FFmpeg finds no video stream, even with a longer probe, in a file that does not look like MPEG.</summary>
    NoStream,

    /// <summary>Every video stream FFmpeg finds is an attached picture: cover art, as in a music file.</summary>
    CoverArtOnly,
}
