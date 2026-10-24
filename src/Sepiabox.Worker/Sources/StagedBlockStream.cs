using Sepiabox.Storage;

namespace Sepiabox.Worker.Sources;

/// <summary>
/// A write-only stream onto a staged upload (S-038): a download writes into it and the bytes go to the store a block at a time, so a
/// video of gigabytes is never held in memory or on the disk (D-100). <see cref="FinishAsync"/> sends the last, partial block.
/// </summary>
public sealed class StagedBlockStream(IStagedUpload upload) : Stream
{
    /// <summary>The block size of a staged upload, as <c>upload</c> uses (D-030).</summary>
    public const int BlockSize = 8 * 1024 * 1024;

    private readonly byte[] _buffer = new byte[BlockSize];
    private int _filled;
    private long _written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // The Google client writes synchronously, on a thread of its own; the store's call is awaited here.
        WriteAsync(buffer.ToArray(), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            var take = Math.Min(buffer.Length, BlockSize - _filled);
            buffer.Span[..take].CopyTo(_buffer.AsSpan(_filled));
            _filled += take;
            _written += take;
            buffer = buffer[take..];
            if (_filled == BlockSize)
            {
                await upload.AppendAsync(_buffer, cancellationToken);
                _filled = 0;
            }
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>Sends what is left in the buffer; call once, after the last write.</summary>
    public async Task FinishAsync(CancellationToken cancellationToken = default)
    {
        if (_filled > 0)
        {
            await upload.AppendAsync(_buffer.AsMemory(0, _filled), cancellationToken);
            _filled = 0;
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
