using ImageMagick;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Tests;

public sealed class QualityAnalyzerTests : IDisposable
{
    private readonly TempFolder _work = new();

    public void Dispose() => _work.Dispose();

    [Fact]
    public void A_blurred_image_has_a_lower_sharpness_than_the_original()
    {
        using var sharp = Scene(seed: 1);
        using var blurred = sharp.Clone();
        blurred.Blur(0, 4);

        Assert.True(QualityAnalyzer.Sharpness(blurred) < QualityAnalyzer.Sharpness(sharp) / 4);
    }

    [Fact]
    public void Measures_the_variance_of_the_laplacian_about_its_mean_not_about_zero()
    {
        // Four pixels wide, three high; the two interior pixels have a Laplacian of -40 and +10: mean -15, variance 625.
        using var image = Gray(4, 3, [0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0]);

        Assert.Equal(625, QualityAnalyzer.Sharpness(image), 6);
    }

    [Fact]
    public void A_constant_curvature_has_no_sharpness_though_its_laplacian_is_not_zero()
    {
        // Columns of x squared: every interior Laplacian is 2, so the mean is 2 and the variance 0 (a mean taken as 0 would say 4).
        var pixels = new byte[5 * 5];
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                pixels[(y * 5) + x] = (byte)(x * x);
            }
        }

        using var image = Gray(5, 5, pixels);

        Assert.Equal(0, QualityAnalyzer.Sharpness(image), 6);
    }

    [Fact]
    public void Sets_a_bit_when_a_pixel_is_brighter_than_the_one_to_its_right_most_significant_bit_first()
    {
        Assert.Equal("0000000000000000", QualityAnalyzer.DHash(Gray(9, 8, Rows(row => row))));
        Assert.Equal("ffffffffffffffff", QualityAnalyzer.DHash(Gray(9, 8, Rows(row => [.. row.Reverse()]))));
        // Only the first comparison of the first row (200 > 10) is true.
        Assert.Equal("8000000000000000", QualityAnalyzer.DHash(Gray(9, 8, Rows((row, y) => y == 0 ? [200, .. row.Skip(1)] : row))));
        // Only the last comparison of the last row (70 > 0) is true.
        Assert.Equal("0000000000000001", QualityAnalyzer.DHash(Gray(9, 8, Rows((row, y) => y == 7 ? [.. row.Take(8), 0] : row))));
    }

    [Fact]
    public void A_flat_image_has_no_sharpness()
    {
        using var flat = new MagickImage(MagickColors.Gray, 400, 300);

        Assert.Equal(0, QualityAnalyzer.Sharpness(flat));
    }

    [Fact]
    public void Measures_the_same_sharpness_whatever_the_size_of_the_image_it_is_given()
    {
        using var large = Scene(seed: 1, width: 2048, height: 1536);
        using var small = large.Clone();
        small.Resize(new MagickGeometry(512, 512) { Greater = true });

        Assert.Equal(QualityAnalyzer.Sharpness(small), QualityAnalyzer.Sharpness(large), 3);
    }

    [Fact]
    public void Leaves_the_image_it_measures_as_it_was()
    {
        using var image = Scene(seed: 1, width: 1024, height: 768);

        QualityAnalyzer.Analyze(image);

        Assert.Equal((1024u, 768u), (image.Width, image.Height));
    }

    [Fact]
    public void A_resized_image_has_a_hash_within_four_bits_of_the_original()
    {
        using var original = Scene(seed: 1, width: 1280, height: 960);
        using var resized = original.Clone();
        resized.Resize(320, 240);

        var distance = QualityAnalyzer.HammingDistance(QualityAnalyzer.DHash(original), QualityAnalyzer.DHash(resized));

        Assert.InRange(distance, 0, 4);
    }

    [Fact]
    public void Different_images_have_hashes_more_than_twenty_bits_apart()
    {
        using var one = Scene(seed: 1);
        using var other = Scene(seed: 2);

        var distance = QualityAnalyzer.HammingDistance(QualityAnalyzer.DHash(one), QualityAnalyzer.DHash(other));

        Assert.True(distance > 20, $"{distance} bits");
    }

    [Fact]
    public void A_hash_is_sixteen_lower_case_hex_characters()
    {
        using var image = Scene(seed: 3);

        var hash = QualityAnalyzer.DHash(image);

        Assert.True(QualityAnalyzer.IsHash(hash));
        Assert.Equal(QualityAnalyzer.HashLength, hash.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdeg")]
    public void Takes_nothing_else_for_a_hash(string? value) => Assert.False(QualityAnalyzer.IsHash(value));

    [Fact]
    public void Counts_the_bits_two_hashes_differ_in()
    {
        Assert.Equal(0, QualityAnalyzer.HammingDistance("0000000000000000", "0000000000000000"));
        Assert.Equal(64, QualityAnalyzer.HammingDistance("0000000000000000", "ffffffffffffffff"));
        Assert.Equal(3, QualityAnalyzer.HammingDistance("0000000000000007", "0000000000000000"));
    }

    [Fact]
    public void Reads_a_webp_preview_from_disk()
    {
        using var image = Scene(seed: 1);
        var path = Path.Combine(_work.Path, "preview.webp");
        image.Write(path, MagickFormat.WebP);

        var quality = QualityAnalyzer.Analyze(path);

        Assert.True(quality.Sharpness > 0);
        Assert.True(QualityAnalyzer.IsHash(quality.DHash));
    }

    [Fact]
    public void The_manifest_step_adds_the_quality_of_a_photo_from_its_preview_and_rounds_the_sharpness()
    {
        var manifest = PhotoWithPreview(seed: 1);

        var outcome = ManifestQualityStep.TryAdd(manifest, _work.Path);

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        var quality = outcome.Manifest!.Quality!;
        Assert.Equal(Math.Round(quality.Sharpness, 2), quality.Sharpness);
        Assert.True(QualityAnalyzer.IsHash(quality.DHash));
    }

    [Fact]
    public void The_manifest_step_leaves_a_measured_photo_and_a_video_alone()
    {
        var measured = PhotoWithPreview(seed: 1) with { Quality = new ManifestQuality(12.5, "0123456789abcdef") };
        var video = PhotoWithPreview(seed: 1) with { Kind = Sepiabox.Pipeline.Media.MediaKind.Video };

        Assert.Same(measured, ManifestQualityStep.TryAdd(measured, _work.Path).Manifest);
        Assert.Null(ManifestQualityStep.TryAdd(video, _work.Path).Manifest!.Quality);
    }

    [Fact]
    public void The_manifest_step_reports_a_preview_that_is_not_on_disk()
    {
        var manifest = TestManifests.Photo(TestManifests.Source("HD da Ana", "2019/praia.jpg"));

        Assert.Equal(EmbeddingStatus.NoDerivative, ManifestQualityStep.TryAdd(manifest, _work.Path).Status);
    }

    [Fact]
    public void The_manifest_step_reports_a_preview_it_cannot_read_and_does_not_throw()
    {
        var manifest = TestManifests.Photo(TestManifests.Source("HD da Ana", "2019/praia.jpg"));
        foreach (var derivative in manifest.Derivatives)
        {
            _work.Write(derivative.Path, [1, 2, 3]);
        }

        var outcome = ManifestQualityStep.TryAdd(manifest, _work.Path);

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
        Assert.Null(outcome.Manifest!.Quality);
    }

    [Fact]
    public void Merging_two_measures_keeps_the_same_one_in_either_order()
    {
        var low = TestManifests.Photo(TestManifests.Source("HD da Ana", "a.jpg")) with { Quality = new ManifestQuality(10, "00000000000000aa") };
        var high = low with { Quality = new ManifestQuality(20, "00000000000000bb") };
        var none = low with { Quality = null };

        Assert.Equal(high.Quality, ManifestMerge.Merge(low, high).Quality);
        Assert.Equal(high.Quality, ManifestMerge.Merge(high, low).Quality);
        Assert.Equal(low.Quality, ManifestMerge.Merge(low, none).Quality);
        Assert.Equal(low.Quality, ManifestMerge.Merge(none, low).Quality);
    }

    [Fact]
    public void Writes_quality_with_a_lower_case_dhash_and_reads_it_back()
    {
        var manifest = TestManifests.Photo(TestManifests.Source("HD da Ana", "a.jpg")) with { Quality = new ManifestQuality(12.34, "0123456789abcdef") };

        var json = ManifestJson.Serialize(manifest);

        Assert.Contains("\"quality\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dhash\": \"0123456789abcdef\"", json.Replace("\":\"", "\": \"", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(manifest.Quality, ManifestJson.Deserialize(json).Quality);
        Assert.DoesNotContain("quality", ManifestJson.Serialize(manifest with { Quality = null }), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"sharpness":-1,"dhash":"0123456789abcdef"}""")]
    [InlineData("""{"sharpness":1,"dhash":"XYZ"}""")]
    [InlineData("""{"sharpness":1}""")]
    [InlineData("""{"sharpness":1,"dhash":"0123456789abcdef","extra":1}""")]
    public void The_schema_refuses_a_malformed_quality(string quality)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(ManifestJson.Serialize(TestManifests.Photo(TestManifests.Source("HD da Ana", "a.jpg"))))!.AsObject();
        node["quality"] = System.Text.Json.Nodes.JsonNode.Parse(quality);

        Assert.False(new ManifestValidator().TryValidate(node.ToJsonString(), out _, out _));
    }

    [Fact]
    public void Merging_equal_sharpness_keeps_the_larger_hash_in_either_order()
    {
        var a = TestManifests.Photo(TestManifests.Source("HD da Ana", "a.jpg")) with { Quality = new ManifestQuality(20, "00000000000000aa") };
        var b = a with { Quality = new ManifestQuality(20, "00000000000000bb") };

        Assert.Equal("00000000000000bb", ManifestMerge.Merge(a, b).Quality!.DHash);
        Assert.Equal("00000000000000bb", ManifestMerge.Merge(b, a).Quality!.DHash);
        Assert.Equal(b.Quality, ManifestQuality.Stronger(a.Quality, b.Quality));
        Assert.Equal(b.Quality, ManifestQuality.Stronger(b.Quality, a.Quality));
    }

    private static MagickImage Gray(int width, int height, byte[] pixels)
    {
        var image = new MagickImage();
        image.ReadPixels(pixels, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, "R"));
        image.ColorSpace = ColorSpace.Gray;
        return image;
    }

    // Eight rows of nine pixels rising 0, 10 ... 80, changed by the function given (the row, and its number).
    private static byte[] Rows(Func<byte[], byte[]> change) => Rows((row, _) => change(row));

    private static byte[] Rows(Func<byte[], int, byte[]> change) =>
        [.. Enumerable.Range(0, 8).SelectMany(y => change([.. Enumerable.Range(0, 9).Select(x => (byte)(x * 10))], y))];

    private Manifest PhotoWithPreview(int seed)
    {
        var manifest = TestManifests.Photo(TestManifests.Source("HD da Ana", "2019/praia.jpg"));
        using var image = Scene(seed);
        foreach (var derivative in manifest.Derivatives)
        {
            var path = Path.Combine(_work.Path, derivative.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            image.Write(path, MagickFormat.WebP);
        }

        return manifest;
    }

    // Blocks of random gray the size of a large detail, with a fine pattern over them: sharp, repeatable, and unlike any other seed.
    private static MagickImage Scene(int seed, int width = 640, int height = 480)
    {
        var random = new Random(seed);
        var cells = new byte[16 * 12];
        random.NextBytes(cells);
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var cell = cells[(y * 12 / height * 16) + (x * 16 / width)];
                var texture = ((x * 16 / width) + (y * 12 / height)) % 2 == 0 && (x / (width / 160)) % 2 == 0 ? 40 : 0;
                pixels[(y * width) + x] = (byte)Math.Clamp(cell + texture, 0, 255);
            }
        }

        var settings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, "R");
        var image = new MagickImage();
        image.ReadPixels(pixels, settings);
        image.ColorSpace = ColorSpace.Gray;
        return image;
    }
}
