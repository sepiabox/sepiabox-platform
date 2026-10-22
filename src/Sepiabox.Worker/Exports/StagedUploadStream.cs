using Sepiabox.Storage;

namespace Sepiabox.Worker.Exports;

/// <summary>
/// A write-only stream onto a staged upload (S-040, D-109): what <see cref="System.IO.Compression.ZipArchive"/> writes
/// goes to the blob in blocks of <see cref="BlockSize"/>, so a zip of any size is never held whole in memory or on disk.
/// The archive writes synchronously, so a full block is appended synchronously too (the job has no synchronization
/// context to deadlock). <see cref="FinishAsync"/> appends the last, partial block; the caller commits.
/// </summary>
public sealed class StagedUploadStream(IStagedUpload upload) : Stream
{
    /// <summary>The block size, as <c>upload</c> uses (D-030).</summary>
    public const int BlockSize = 8 * 1024 * 1024;

    private readonly byte[] _buffer = new byte[BlockSize];
    private int _filled;
    private long _length;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    /// <summary>The bytes written so far.</summary>
    public override long Length => _length;

    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var take = Math.Min(buffer.Length, BlockSize - _filled);
            buffer[..take].CopyTo(_buffer.AsSpan(_filled));
            _filled += take;
            _length += take;
            buffer = buffer[take..];
            if (_filled == BlockSize)
            {
                Append();
            }
        }
    }

    /// <summary>Appends what is left in the buffer, as the last block.</summary>
    public async Task FinishAsync(CancellationToken cancellationToken = default)
    {
        if (_filled > 0)
        {
            await upload.AppendAsync(_buffer.AsMemory(0, _filled), cancellationToken);
            _filled = 0;
        }
    }

    // A block that is only part-way is not appended: a block is full or the last.
    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Append()
    {
        upload.AppendAsync(_buffer.AsMemory(0, _filled)).GetAwaiter().GetResult();
        _filled = 0;
    }
}
