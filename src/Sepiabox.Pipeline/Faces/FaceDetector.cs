using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Models;

namespace Sepiabox.Pipeline.Faces;

/// <summary>A face in an image: its box relative to the image size (0–1), the detector's score and its SFace embedding.</summary>
public sealed record DetectedFace(double X, double Y, double Width, double Height, double Score, float[] Embedding);

/// <summary>Finds the faces in an image and describes each one by a vector that is close for the same person.</summary>
public interface IFaceDetector
{
    /// <summary>
    /// The faces in the image at <paramref name="path"/>, best first. Throws <see cref="EmbeddingException"/> when
    /// a model gives no usable output.
    /// </summary>
    IReadOnlyList<DetectedFace> Detect(string path);
}

/// <summary>
/// Faces with YuNet and SFace from the OpenCV Zoo (D-016, S-018): detection with a score of at least
/// <see cref="MinimumScore"/> and boxes of at least <see cref="MinimumSide"/> pixels, five-point alignment to
/// 112×112 and a 128-value L2-normalized SFace embedding. One instance per run; sessions are expensive to create.
/// </summary>
public sealed class FaceDetector : IFaceDetector, IDisposable
{
    public const double MinimumScore = 0.8;

    /// <summary>The shorter side of a face box, in pixels of the image detection runs on.</summary>
    public const double MinimumSide = 40;

    public const int Dimensions = 128;

    private const string DetectorInput = "input";
    private const string RecognizerInput = "data";
    private const string RecognizerOutput = "fc1";

    private readonly InferenceSession _detector;
    private readonly InferenceSession _recognizer;

    private FaceDetector(InferenceSession detector, InferenceSession recognizer)
    {
        _detector = detector;
        _recognizer = recognizer;
    }

    /// <summary>
    /// Loads both pinned models from <paramref name="modelFolder"/>, or returns null with a warning that says why,
    /// as <see cref="ModelSession.TryOpen"/> does. A run goes on without faces rather than failing every photo.
    /// </summary>
    public static FaceDetector? TryCreate(string modelFolder, out string? warning)
    {
        const string Consequence = "so no faces were detected";
        const string Download =
            "Run `sepiabox-ingest models download` and process again with --faces: faces are added from the work folder "
            + "without reading the drive again.";
        var detector = ModelSession.TryOpen(
            modelFolder, ModelCatalog.Get(ModelCatalog.YuNet), "The YuNet face detection model", Consequence, Download,
            [.. YuNet.OutputNames], out warning);
        if (detector is null)
        {
            return null;
        }

        var recognizer = ModelSession.TryOpen(
            modelFolder, ModelCatalog.Get(ModelCatalog.SFace), "The SFace face recognition model", Consequence, Download,
            [RecognizerOutput], out warning);
        if (recognizer is null)
        {
            detector.Dispose();
            return null;
        }

        return new FaceDetector(detector, recognizer);
    }

    public IReadOnlyList<DetectedFace> Detect(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaAction.Remove);
        image.ColorSpace = ColorSpace.sRGB;
        var width = (int)image.Width;
        var height = (int)image.Height;
        var rgb = Pixels(image);

        var faces = new List<DetectedFace>();
        foreach (var candidate in Find(image))
        {
            if (Math.Min(candidate.Width, candidate.Height) < MinimumSide)
            {
                continue;
            }

            var crop = FaceAlignment.Crop(rgb, width, height, FaceAlignment.TransformFor(candidate.Landmarks));
            var left = Math.Clamp(candidate.X, 0, width);
            var top = Math.Clamp(candidate.Y, 0, height);
            var right = Math.Clamp(candidate.X + candidate.Width, 0, width);
            var bottom = Math.Clamp(candidate.Y + candidate.Height, 0, height);
            faces.Add(new DetectedFace(
                left / width, top / height, (right - left) / width, (bottom - top) / height, candidate.Score, Embed(crop)));
        }

        return faces;
    }

    /// <summary>The SFace embedding of an aligned 112×112 RGB crop, L2-normalized.</summary>
    public float[] Embed(byte[] alignedRgb)
    {
        const int Plane = FaceAlignment.Size * FaceAlignment.Size;
        if (alignedRgb.Length != Plane * 3)
        {
            throw new ArgumentException($"Expected {Plane * 3} bytes of 112×112 RGB, got {alignedRgb.Length}.", nameof(alignedRgb));
        }

        // OpenCV feeds SFace RGB values 0–255 with no mean or scale (blobFromImage with swapRB).
        var tensor = new DenseTensor<float>([1, 3, FaceAlignment.Size, FaceAlignment.Size]);
        for (var pixel = 0; pixel < Plane; pixel++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                tensor.Buffer.Span[(channel * Plane) + pixel] = alignedRgb[(pixel * 3) + channel];
            }
        }

        using var results = _recognizer.Run([NamedOnnxValue.CreateFromTensor(RecognizerInput, tensor)], [RecognizerOutput]);
        var embedding = results[0].AsEnumerable<float>().ToArray();
        if (embedding.Length != Dimensions)
        {
            throw new EmbeddingException($"The SFace model returned {embedding.Length} values instead of {Dimensions}.");
        }

        EmbeddingCodec.Normalize(embedding);
        return embedding;
    }

    public void Dispose()
    {
        _detector.Dispose();
        _recognizer.Dispose();
    }

    /// <summary>
    /// Runs YuNet on the image shrunk (or enlarged) to fit its 640×640 input, keeping the aspect ratio and padding the
    /// bottom and right with black, and gives the faces back in the image's own pixels.
    /// </summary>
    private List<FaceCandidate> Find(MagickImage image)
    {
        const int Size = YuNet.InputSize;
        var scale = Math.Min((double)Size / image.Width, (double)Size / image.Height);
        var width = Math.Clamp((int)Math.Round(image.Width * scale), 1, Size);
        var height = Math.Clamp((int)Math.Round(image.Height * scale), 1, Size);
        byte[] rgb;
        using (var resized = image.Clone())
        {
            resized.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            rgb = Pixels(resized);
        }

        // BGR, channel first, as OpenCV's blobFromImage gives it.
        const int Plane = Size * Size;
        var tensor = new DenseTensor<float>([1, 3, Size, Size]);
        var buffer = tensor.Buffer.Span;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = ((y * width) + x) * 3;
                var target = (y * Size) + x;
                buffer[target] = rgb[source + 2];
                buffer[Plane + target] = rgb[source + 1];
                buffer[(2 * Plane) + target] = rgb[source];
            }
        }

        var names = YuNet.OutputNames.ToList();
        using var results = _detector.Run([NamedOnnxValue.CreateFromTensor(DetectorInput, tensor)], names);
        var byName = results.ToDictionary(r => r.Name, r => r.AsEnumerable<float>().ToArray(), StringComparer.Ordinal);
        var outputs = YuNet.Strides
            .Select(stride => new YuNet.StrideOutput(
                stride, byName[$"cls_{stride}"], byName[$"obj_{stride}"], byName[$"bbox_{stride}"], byName[$"kps_{stride}"]))
            .ToList();
        return [.. YuNet.Decode(outputs, MinimumScore).Select(face => face.Scaled((double)image.Width / width, (double)image.Height / height))];
    }

    private static byte[] Pixels(IMagickImage<byte> image)
    {
        using var pixels = image.GetPixels();
        return pixels.ToByteArray(PixelMapping.RGB) ?? throw new EmbeddingException("The image has no pixels.");
    }
}
