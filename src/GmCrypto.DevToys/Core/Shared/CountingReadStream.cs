namespace GmCrypto.DevToys.Core;

/// <summary>
/// 只读流包装：统计已读字节并按总长度上报百分比。
/// SM3 的流式实现只依赖 Read，包一层不影响摘要结果；总长度未知时不上报（进度条保持 0）。
/// </summary>
public sealed class CountingReadStream : Stream
{
    private readonly Stream _inner;

    private readonly long? _totalLength;

    private readonly IProgress<double>? _progress;

    private long _bytesRead;

    public CountingReadStream(Stream inner, long? totalLength, IProgress<double>? progress)
    {
        _inner = inner;
        _totalLength = totalLength;
        _progress = progress;
    }

    public long BytesRead => _bytesRead;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        Report(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        int read = _inner.Read(buffer);
        Report(read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Report(int read)
    {
        if (read <= 0)
        {
            return;
        }

        _bytesRead += read;
        if (_totalLength is long total && total > 0)
        {
            _progress?.Report(Math.Min(100d, _bytesRead * 100d / total));
        }
    }
}
