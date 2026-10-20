namespace Sepiabox.Pipeline.Media;

/// <summary>
/// Why a file with a video extension is left out as holding no video (D-045). The names are stored in each work
/// folder's state, so they are never renamed.
/// </summary>
public enum NoVideoReason
{
    /// <summary>Not a reason; the default, so an unset value is never mistaken for one.</summary>
    Unknown = 0,

    /// <summary>A tracker-music module, recognised by its ProTracker-family tag.</summary>
    TrackerModule,

    /// <summary>FFmpeg finds no video stream, even with a longer probe, in a file that does not look like MPEG.</summary>
    NoStream,

    /// <summary>Every video stream FFmpeg finds is an attached picture: cover art, as in a music file.</summary>
    CoverArtOnly,
}
