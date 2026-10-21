using Sepiabox.Pipeline.Video;

namespace Sepiabox.Ingest.Processing;

/// <summary>What <c>process</c> says when the encoder it was asked for cannot encode a generated clip (D-087).</summary>
public static class EncoderCheckReport
{
    public static string Message(VideoEncoderUnavailableException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Encoder == VideoEncoder.Nvenc
            ? $"""
                The NVIDIA encoder (--hw nvenc) cannot encode on this computer, so nothing was processed. FFmpeg said:
                  {failure.Message}
                Update the NVIDIA driver to the version FFmpeg asks for (or a newer one) and run the same command again, or run it without --hw nvenc to encode on the processor (slower).
                """
            : $"""
                The video encoder cannot encode on this computer, so nothing was processed. FFmpeg said:
                  {failure.Message}
                """;
    }
}
