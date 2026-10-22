using System.Net;
using Sepiabox.Catalog;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// <see cref="SignedLinkPreviewSource"/> (S-041, D-099): which derivative it fetches, and what it does with a response
/// that is too big, a redirect or an error. A fake handler stands for storage; the catalog is a real PostgreSQL.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PreviewSourceTests(CatalogFixture catalog)
{
    [Fact]
    public async Task Fetches_the_1280_preview_before_the_2048_one_before_a_poster()
    {
        var all = await SourceAsync(Photo(1, "thumb-256", "poster", "preview-2048", "preview-1280"));
        var large = await SourceAsync(Photo(2, "thumb-256", "poster", "preview-2048"));
        var poster = await SourceAsync(Photo(3, "thumb-256", "poster"));

        await all.Source.ReadAsync(Ids.Of(1));
        await large.Source.ReadAsync(Ids.Of(2));
        await poster.Source.ReadAsync(Ids.Of(3));

        Assert.EndsWith("/preview-1280.webp", all.Handler.Requested.Single().AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/preview-2048.webp", large.Handler.Requested.Single().AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/poster.webp", poster.Handler.Requested.Single().AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gives_null_for_an_asset_that_is_not_there_or_has_no_preview()
    {
        var (source, handler) = await SourceAsync(Photo(1, "thumb-256"));

        Assert.Null(await source.ReadAsync(Ids.Of(1)));
        Assert.Null(await source.ReadAsync(Ids.Of(9)));
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task Gives_the_bytes_of_the_preview()
    {
        var (source, _) = await SourceAsync(Photo(1, "preview-1280"));

        Assert.Equal([1, 2, 3], await source.ReadAsync(Ids.Of(1)));
    }

    [Fact]
    public async Task Refuses_a_declared_length_over_the_cap_without_reading_the_body()
    {
        var (source, handler) = await SourceAsync(
            Photo(1, "preview-1280"),
            _ =>
            {
                var content = new ByteArrayContent([1, 2, 3]);
                content.Headers.ContentLength = SignedLinkPreviewSource.MaxBytes + 1;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(Ids.Of(1)));
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task Stops_reading_a_body_with_no_length_when_it_goes_past_the_cap()
    {
        long served = 0;
        var (source, _) = await SourceAsync(
            Photo(1, "preview-1280"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream(count => served = count)) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(Ids.Of(1)));

        // It read a little past the cap and stopped: it did not go on for ever.
        Assert.InRange(served, SignedLinkPreviewSource.MaxBytes, SignedLinkPreviewSource.MaxBytes + (1024 * 1024));
    }

    [Fact]
    public async Task Does_not_take_a_redirect_for_a_preview()
    {
        var (source, handler) = await SourceAsync(
            Photo(1, "preview-1280"),
            _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://elsewhere.test/x.webp") } });

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(Ids.Of(1)));
        Assert.Single(handler.Requested);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Turns_an_error_answer_into_an_invalid_operation(HttpStatusCode status)
    {
        var (source, _) = await SourceAsync(Photo(1, "preview-1280"), _ => new HttpResponseMessage(status));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(Ids.Of(1)));
        Assert.IsType<HttpRequestException>(thrown.InnerException);
    }

    private static Asset Photo(int n, params string[] derivatives)
    {
        var photo = CatalogData.Photo(Ids.Of(n), new DateTime(2020, 1, 1), withDerivatives: false);
        photo.Derivatives = [.. derivatives.Select(name => new Derivative
        {
            AssetId = Ids.Of(n), Name = name, BlobPath = $"{Ids.Of(n)}/{name}.webp", Width = 1, Height = 1, ContentType = "image/webp",
        })];
        return photo;
    }

    private async Task<(SignedLinkPreviewSource Source, RecordingHandler Handler)> SourceAsync(
        Asset asset, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        var handler = new RecordingHandler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }));
        var source = new SignedLinkPreviewSource(new TestContexts(catalog.ConnectionStringFor(db)), new Links(), new Clients(handler));
        return (source, handler);
    }

    private sealed class Links : ISasUrlFactory
    {
        public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Uri($"https://storage.test/derivatives/{blobPath}?sig=x"));

        public Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default) => ForDerivativeAsync(blobPath, cancellationToken);

        public Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    // Not seekable, so the content has no length: what a chunked response looks like.
    private sealed class EndlessStream(Action<long> served) : Stream
    {
        private long _count;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Fill(count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Fill(buffer.Length));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Fill(int count)
        {
            _count += count;
            served(_count);
            return count;
        }
    }
}
