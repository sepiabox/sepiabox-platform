using Sepiabox.Pipeline.Faces;

namespace Sepiabox.Pipeline.Tests;

public sealed class FaceAlignmentTests
{
    // Reference to image: scale 2.5, rotated 15°, moved to (100, 80), as a tilted face in a photo would be.
    private static readonly SimilarityTransform s_referenceToImage = FromParts(2.5, 15, 100, 80);

    [Fact]
    public void Estimates_the_similarity_that_moved_the_points()
    {
        var landmarks = FaceAlignment.ArcFaceReference.Select(s_referenceToImage.Apply).ToList();

        var estimated = SimilarityTransform.Estimate(FaceAlignment.ArcFaceReference, landmarks);

        AssertClose(s_referenceToImage, estimated);
    }

    [Fact]
    public void Moves_the_landmarks_exactly_onto_the_arcface_reference_points()
    {
        var landmarks = FaceAlignment.ArcFaceReference.Select(s_referenceToImage.Apply).ToList();

        var transform = FaceAlignment.TransformFor(landmarks);

        for (var i = 0; i < landmarks.Count; i++)
        {
            var moved = transform.Apply(landmarks[i]);
            Assert.Equal(FaceAlignment.ArcFaceReference[i].X, moved.X, 6);
            Assert.Equal(FaceAlignment.ArcFaceReference[i].Y, moved.Y, 6);
        }

        Assert.Equal(1 / 2.5, transform.Scale, 6);
    }

    [Fact]
    public void Fits_noisy_landmarks_in_the_least_squares_sense()
    {
        // Each landmark off by up to a pixel: the fit is still close to the true transform, and no closer one exists.
        double[] noise = [0.8, -0.5, -0.9, 0.3, 0.4, 1.0, -0.6, -0.2, 0.7, -0.8];
        var landmarks = FaceAlignment.ArcFaceReference
            .Select(s_referenceToImage.Apply)
            .Select((p, i) => new FacePoint(p.X + noise[2 * i], p.Y + noise[(2 * i) + 1]))
            .ToList();

        var fitted = SimilarityTransform.Estimate(FaceAlignment.ArcFaceReference, landmarks);

        Assert.Equal(s_referenceToImage.Scale, fitted.Scale, 1);
        var best = Residual(fitted, landmarks);
        Assert.True(best <= Residual(s_referenceToImage, landmarks), "the fit should be at least as good as the true transform");
        Assert.True(best <= Residual(fitted with { Tx = fitted.Tx + 0.01 }, landmarks));
        Assert.True(best <= Residual(fitted with { A = fitted.A * 1.001 }, landmarks));
        Assert.True(best <= Residual(fitted with { B = fitted.B + 0.001 }, landmarks));
    }

    [Fact]
    public void The_inverse_undoes_the_transform()
    {
        var point = new FacePoint(12.5, -40.25);

        var back = s_referenceToImage.Inverse().Apply(s_referenceToImage.Apply(point));

        Assert.Equal(point.X, back.X, 9);
        Assert.Equal(point.Y, back.Y, 9);
    }

    [Fact]
    public void Crops_each_pixel_from_where_the_transform_sends_it()
    {
        // A gradient, so the expected value of every crop pixel is known: red is x / 2, green y / 2, rounded down,
        // which with bilinear sampling and rounding keeps each value within 1.5 below and 1 above.
        const int Width = 500, Height = 500;
        var rgb = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                rgb[((y * Width) + x) * 3] = (byte)(x / 2);
                rgb[(((y * Width) + x) * 3) + 1] = (byte)(y / 2);
                rgb[(((y * Width) + x) * 3) + 2] = 128;
            }
        }

        var landmarks = FaceAlignment.ArcFaceReference.Select(s_referenceToImage.Apply).ToList();
        var crop = FaceAlignment.Crop(rgb, Width, Height, FaceAlignment.TransformFor(landmarks));

        Assert.Equal(FaceAlignment.Size * FaceAlignment.Size * 3, crop.Length);
        var checkedPixels = 0;
        for (var v = 0; v < FaceAlignment.Size; v++)
        {
            for (var u = 0; u < FaceAlignment.Size; u++)
            {
                var source = s_referenceToImage.Apply(new FacePoint(u, v));
                if (source.X < 1 || source.Y < 1 || source.X > Width - 2 || source.Y > Height - 2)
                {
                    continue;
                }

                var offset = ((v * FaceAlignment.Size) + u) * 3;
                Assert.InRange(crop[offset], (source.X / 2) - 1.5, (source.X / 2) + 1);
                Assert.InRange(crop[offset + 1], (source.Y / 2) - 1.5, (source.Y / 2) + 1);
                Assert.Equal(128, crop[offset + 2]);
                checkedPixels++;
            }
        }

        Assert.Equal(FaceAlignment.Size * FaceAlignment.Size, checkedPixels);
    }

    [Fact]
    public void Fills_what_falls_outside_the_image_with_black()
    {
        var white = Enumerable.Repeat((byte)255, 20 * 20 * 3).ToArray();
        // The reference eyes land on a 20×20 white image; the lower part of the crop falls below it.
        var transform = SimilarityTransform.Estimate(
            [new FacePoint(5, 5), new FacePoint(15, 5)],
            [FaceAlignment.ArcFaceReference[0], FaceAlignment.ArcFaceReference[1]]);

        var crop = FaceAlignment.Crop(white, 20, 20, transform);

        Assert.Equal(0, crop[((111 * FaceAlignment.Size) + 56) * 3]);
        var eye = (int)Math.Round(FaceAlignment.ArcFaceReference[0].Y);
        Assert.Equal(255, crop[((eye * FaceAlignment.Size) + 50) * 3]);
    }

    [Fact]
    public void Refuses_landmarks_that_are_not_five_points()
    {
        Assert.Throws<ArgumentException>(() => FaceAlignment.TransformFor([new FacePoint(0, 0), new FacePoint(1, 1)]));
    }

    [Fact]
    public void Refuses_points_that_are_all_the_same()
    {
        FacePoint[] same = [new(3, 3), new(3, 3), new(3, 3), new(3, 3), new(3, 3)];

        Assert.Throws<ArgumentException>(() => FaceAlignment.TransformFor(same));
    }

    private static SimilarityTransform FromParts(double scale, double degrees, double tx, double ty)
    {
        var radians = degrees * Math.PI / 180;
        return new SimilarityTransform(scale * Math.Cos(radians), scale * Math.Sin(radians), tx, ty);
    }

    private static double Residual(SimilarityTransform transform, List<FacePoint> landmarks) =>
        FaceAlignment.ArcFaceReference.Select((p, i) =>
        {
            var moved = transform.Apply(p);
            return Math.Pow(moved.X - landmarks[i].X, 2) + Math.Pow(moved.Y - landmarks[i].Y, 2);
        }).Sum();

    private static void AssertClose(SimilarityTransform expected, SimilarityTransform actual)
    {
        Assert.Equal(expected.A, actual.A, 9);
        Assert.Equal(expected.B, actual.B, 9);
        Assert.Equal(expected.Tx, actual.Tx, 6);
        Assert.Equal(expected.Ty, actual.Ty, 6);
    }
}
