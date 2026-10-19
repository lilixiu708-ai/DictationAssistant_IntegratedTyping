using ManagedBass;
using System.IO;
using System.Runtime.InteropServices;

namespace DictationAssistant.App.Audio;

public sealed class BassDecodeStream : Stream
{
    private const int ScratchBufferSize = 4096;

    private static readonly FileProcedures CustomStreamProcedures = new()
    {
        Close = OnCustomStreamClose,
        Length = OnCustomStreamLength,
        Read = OnCustomStreamRead,
        Seek = OnCustomStreamSeek
    };

    private int _channel;
    private long _position;
    private int _scratchStart;
    private int _scratchLength;
    private readonly byte[] _scratchBuffer = new byte[ScratchBufferSize];
    private bool _disposed;

    private readonly GCHandle _customStreamStateHandle;

    public PcmFormatInfo Format { get; }

    public BassDecodeStream(Stream baseStream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(baseStream);

        BassInitialization.EnsureInitialized();

        var customStreamState = new CustomStreamState(baseStream, leaveOpen);
        var customStreamStateHandle = GCHandle.Alloc(customStreamState, GCHandleType.Normal);

        try
        {
            var channel = Bass.CreateStream(
                StreamSystem.NoBuffer,
                BassFlags.Decode,
                CustomStreamProcedures,
                GCHandle.ToIntPtr(customStreamStateHandle));

            if (channel == 0)
            {
                throw new InvalidOperationException($"Failed to create BASS stream from input stream: {Bass.LastError}");
            }

            _channel = channel;
            _customStreamStateHandle = customStreamStateHandle;

            var channelInfo = Bass.ChannelGetInfo(channel);
            Format = new PcmFormatInfo(
                channelInfo.Frequency,
                channelInfo.Channels,
                channelInfo.Resolution switch
                {
                    Resolution.Byte => PcmSampleFormat.U8,
                    _ => PcmSampleFormat.S16LE
                });
        }
        catch
        {
            if (customStreamStateHandle.IsAllocated)
            {
                customStreamStateHandle.Free();
            }

            if (!leaveOpen)
            {
                baseStream.Dispose();
            }

            throw;
        }
    }

    public override bool CanRead => !_disposed;
    public override bool CanWrite => false;
    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> destination)
    {
        if (_channel == 0 || _disposed || destination.IsEmpty)
        {
            return 0;
        }

        var totalRead = 0;

        while (totalRead < destination.Length)
        {
            var remaining = destination[totalRead..];
            var scratchUnread = _scratchLength - _scratchStart;

            if (scratchUnread > 0)
            {
                var copyLength = Math.Min(scratchUnread, remaining.Length);
                _scratchBuffer.AsSpan(_scratchStart, copyLength).CopyTo(remaining);

                _scratchStart += copyLength;
                if (_scratchStart >= _scratchLength)
                {
                    _scratchStart = 0;
                    _scratchLength = 0;
                }

                totalRead += copyLength;
                _position += copyLength;
                continue;
            }

            if (remaining.Length >= _scratchBuffer.Length)
            {
                var decoded = ReadFromBass(remaining);
                if (decoded <= 0)
                {
                    break;
                }

                totalRead += decoded;
                _position += decoded;
                continue;
            }

            _scratchLength = ReadFromBass(_scratchBuffer);
            _scratchStart = 0;
            if (_scratchLength <= 0)
            {
                _scratchLength = 0;
                break;
            }
        }

        return totalRead;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<int>(cancellationToken);
        }

        try
        {
            return ValueTask.FromResult(Read(buffer.Span));
        }
        catch (Exception ex)
        {
            return ValueTask.FromException<int>(ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;

        try
        {
            if (_channel != 0)
            {
                Bass.StreamFree(_channel);
                _channel = 0;
            }
        }
        finally
        {
            ReleaseCustomStreamState();
            base.Dispose(disposing);
        }
    }

    private unsafe int ReadFromBass(Span<byte> destination)
    {
        if (destination.IsEmpty || _channel == 0)
        {
            return 0;
        }

        fixed (byte* ptr = destination)
        {
            var read = Bass.ChannelGetData(_channel, (IntPtr)ptr, destination.Length);
            return read > 0 ? read : 0;
        }
    }

    private void ReleaseCustomStreamState()
    {
        if (!_customStreamStateHandle.IsAllocated)
        {
            return;
        }

        try
        {
            if (_customStreamStateHandle.Target is CustomStreamState customStreamState && !customStreamState.LeaveOpen)
            {
                customStreamState.Stream.Dispose();
            }
        }
        finally
        {
            _customStreamStateHandle.Free();
        }
    }

    private static CustomStreamState? GetCustomStreamState(IntPtr user)
    {
        if (user == IntPtr.Zero)
        {
            return null;
        }

        var handle = GCHandle.FromIntPtr(user);
        return handle.Target as CustomStreamState;
    }

    private static void OnCustomStreamClose(IntPtr user)
    {
        // Stream lifetime is controlled by BassDecodeStream.Dispose.
    }

    private static long OnCustomStreamLength(IntPtr user)
    {
        var customStreamState = GetCustomStreamState(user);
        if (customStreamState is null || !customStreamState.Stream.CanSeek)
        {
            return 0;
        }

        try
        {
            return customStreamState.Stream.Length;
        }
        catch
        {
            return 0;
        }
    }

    private static unsafe int OnCustomStreamRead(IntPtr buffer, int length, IntPtr user)
    {
        if (length <= 0 || buffer == IntPtr.Zero)
        {
            return 0;
        }

        var customStreamState = GetCustomStreamState(user);
        if (customStreamState is null)
        {
            return 0;
        }

        try
        {
            var destination = new Span<byte>(buffer.ToPointer(), length);
            var read = customStreamState.Stream.Read(destination);
            return read == 0 ? -1 : read;
        }
        catch
        {
            return 0;
        }
    }

    private static bool OnCustomStreamSeek(long offset, IntPtr user)
    {
        var customStreamState = GetCustomStreamState(user);
        if (customStreamState is null || !customStreamState.Stream.CanSeek)
        {
            return false;
        }

        try
        {
            customStreamState.Stream.Seek(offset, SeekOrigin.Begin);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class CustomStreamState
    {
        public CustomStreamState(Stream stream, bool leaveOpen)
        {
            Stream = stream;
            LeaveOpen = leaveOpen;
        }

        public Stream Stream { get; }

        public bool LeaveOpen { get; }
    }
}
