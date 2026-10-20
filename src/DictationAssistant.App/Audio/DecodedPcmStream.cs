// Modified 2026-10-20: stream decoded audio without proprietary native callbacks.
// SPDX-License-Identifier: AGPL-3.0-or-later
using NAudio.Wave;

namespace DictationAssistant.App.Audio;

/// <summary>Exposes a bounded, frame-aligned decoder as ordinary forward-only PCM.</summary>
internal sealed class DecodedPcmStream : Stream
{
    private readonly IWaveProvider _provider;
    private readonly WaveStream _reader;
    private readonly Stream? _input;
    private readonly byte[] _scratch;
    private int _scratchOffset;
    private int _scratchCount;
    private long _position;
    private bool _disposed;

    public DecodedPcmStream(IWaveProvider provider, WaveStream reader, Stream? input)
    {
        _provider = provider;
        _reader = reader;
        _input = input;
        var blockAlign = provider.WaveFormat.BlockAlign;
        if (blockAlign <= 0 || blockAlign > 4096)
            throw new InvalidDataException("解码器返回了无效的 PCM 帧大小。");
        _scratch = new byte[Math.Max(1, 16384 / blockAlign) * blockAlign];
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var total = 0;
        while (total < destination.Length)
        {
            if (_scratchOffset == _scratchCount)
            {
                _scratchCount = _provider.Read(_scratch, 0, _scratch.Length);
                _scratchOffset = 0;
                if ((uint)_scratchCount > (uint)_scratch.Length ||
                    _scratchCount % _provider.WaveFormat.BlockAlign != 0)
                    throw new InvalidDataException("解码器返回了无效或不完整的 PCM 帧。");
                if (_scratchCount == 0) break;
            }
            var copy = Math.Min(_scratchCount - _scratchOffset, destination.Length - total);
            _scratch.AsSpan(_scratchOffset, copy).CopyTo(destination[total..]);
            _scratchOffset += copy;
            total += copy;
            _position += copy;
        }
        return total;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<int>(cancellationToken);
        try { return ValueTask.FromResult(Read(buffer.Span)); }
        catch (Exception exception) { return ValueTask.FromException<int>(exception); }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                try { _reader.Dispose(); }
                finally { _input?.Dispose(); }
            }
        }
        base.Dispose(disposing);
    }
}
