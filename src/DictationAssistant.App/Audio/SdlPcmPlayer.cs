using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using Hexa.NET.SDL2;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DictationAssistant.App.Audio;

public sealed class SdlPcmPlayer : IAudioPlayer, IDisposable
{
    private const uint SDL_INIT_AUDIO = 0x10;

    private const ushort AUDIO_U8 = 0x0008;
    private const ushort AUDIO_S16LSB = 0x8010;

    private static readonly object InitLock = new();
    private static int s_instanceCount;
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private int _disposed;

    private int _sdlVolume = 128; // Default volume is 100%

    public int Volume
    {
        get => (int)Math.Round(_sdlVolume / 128.0 * 100);
        set
        {
            if (value < 0 || value > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Volume must be between 0 and 100.");
            }
            _sdlVolume = (int)Math.Round(value / 100.0 * 128);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_AudioCallback(IntPtr userdata, IntPtr stream, int len);

    public SdlPcmPlayer()
    {
        lock (InitLock)
        {
            if (s_instanceCount == 0)
            {
                var result = SDL.InitSubSystem(SDL_INIT_AUDIO);
                if (result < 0)
                {
                    throw new InvalidOperationException($"SDL audio init failed: {SDL.GetErrorS()}");
                }
            }

            s_instanceCount++;
        }
    }

    public async Task PlayAsync(PcmAudio audio, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        await _playbackGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();
            await PlayCoreAsync(audio, ct).ConfigureAwait(false);
        }
        finally
        {
            // A following playback or shutdown may proceed only after the
            // device has joined its callback and both handles are released.
            _playbackGate.Release();
        }
    }

    private async Task PlayCoreAsync(PcmAudio audio, CancellationToken ct)
    {
        if (!audio.Data.CanRead)
            throw new ArgumentException("PCM data must be a readable stream.", nameof(audio));
        if (audio.Format.SampleRate <= 0 || audio.Format.Channels is < 1 or > byte.MaxValue)
            throw new ArgumentException("PCM sample rate and channel count are invalid.", nameof(audio));
        var sdlFormat = GetSdlFormat(audio.Format.SampleFormat);
        if (sdlFormat == 0)
        {
            throw new NotSupportedException($"Unsupported PCM format: {audio.Format.SampleFormat}");
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var userdata = new AudioCallbackData
        {
            PcmStream = audio.Data,
            SdlFormat = sdlFormat,
            Tcs = tcs
        };

        // SDL may invoke the callback immediately after unpausing. Pin both
        // callback delegate and userdata BEFORE opening/starting the device.
        var callback = new SDL_AudioCallback(AudioCallback);
        var callbackHandle = GCHandle.Alloc(callback);
        var userdataHandle = GCHandle.Alloc(userdata, GCHandleType.Normal);
        uint device = 0;
        try
        {
            var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
            device = OpenDevice(audio.Format, sdlFormat, callbackPtr, userdataHandle, out var obtained);
            if (device == 0)
            {
                throw new InvalidOperationException($"SDL open audio device failed: {SDL.GetErrorS()}");
            }
            if (obtained.Format != sdlFormat || obtained.Freq != audio.Format.SampleRate ||
                obtained.Channels != audio.Format.Channels)
            {
                throw new NotSupportedException("SDL device returned a different PCM format.");
            }
            // SDL normally requests only a few KiB. Reject an invalid device
            // size before allocating, and reuse this buffer for volume mixing.
            if (obtained.Size is 0 or > 16 * 1024 * 1024)
                throw new InvalidOperationException("SDL device returned an invalid buffer size.");
            userdata.MixBuffer = new byte[checked((int)obtained.Size)];

            using var ctr = ct.Register(() => tcs.TrySetCanceled(ct));
            ct.ThrowIfCancellationRequested();
            SDL.PauseAudioDevice(device, 0);
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            // CloseAudioDevice stops and joins its callback thread. Do this
            // before releasing either managed GCHandle.
            if (device != 0) SDL.CloseAudioDevice(device);
            userdataHandle.Free();
            callbackHandle.Free();
        }
    }

    private unsafe void AudioCallback(IntPtr udata, IntPtr streamPtr, int len)
    {
        if (streamPtr == IntPtr.Zero || len <= 0)
        {
            return;
        }

        Span<byte> streamSpan = new(streamPtr.ToPointer(), len);
        AudioCallbackData? data = null;
        byte silence = 0;
        // Plugin streams can throw during Read. Never allow a managed
        // exception to cross this reverse P/Invoke boundary into SDL.
        try
        {
            var handle = GCHandle.FromIntPtr(udata);
            data = handle.IsAllocated ? handle.Target as AudioCallbackData : null;
            if (data is null)
                throw new InvalidOperationException("Invalid SDL audio callback userdata.");

            silence = SdlFormatIsUnsigned(data.SdlFormat) ? (byte)128 : (byte)0;
            streamSpan.Fill(silence);
            if (len > data.MixBuffer.Length)
                throw new InvalidOperationException("SDL callback exceeded its negotiated buffer size.");
            if (data.Tcs.Task.IsCompleted)
                return;
            if (data.FinalBufferQueued)
            {
                // The previously queued final PCM buffer has now drained.
                data.Tcs.TrySetResult();
                return;
            }

            var numOfRead = 0;
            var volume = Volatile.Read(ref _sdlVolume);
            if (volume < 128)
            {
                var buffer = data.MixBuffer;
                while (numOfRead < len)
                {
                    var t = data.PcmStream.Read(buffer, numOfRead, len - numOfRead);
                    if ((uint)t > (uint)(len - numOfRead))
                        throw new InvalidDataException("PCM stream returned an invalid read count.");
                    numOfRead += t;
                    if (t == 0)
                        break;
                }
                // Mix the PCM data with volume control into the stream buffer
                fixed (byte* srcPtr = buffer)
                {
                    SDL.MixAudioFormat((byte*)streamPtr, srcPtr, data.SdlFormat, (uint)numOfRead, volume);
                }
            }
            else
            {
                // Read PCM data directly into the stream buffer without volume control
                while (numOfRead < len)
                {
                    var t = data.PcmStream.Read(streamSpan[numOfRead..]);
                    if ((uint)t > (uint)(len - numOfRead))
                        throw new InvalidDataException("PCM stream returned an invalid read count.");
                    numOfRead += t;
                    if (t == 0)
                        break;
                }
            }
            if (numOfRead < len)
            {
                // A partial final buffer must drain before PlayAsync completes.
                if (numOfRead == 0) data.Tcs.TrySetResult();
                else data.FinalBufferQueued = true;
            }
        }
        catch (Exception ex)
        {
            streamSpan.Fill(silence);
            data?.Tcs.TrySetException(ex);
        }
    }

    private static unsafe uint OpenDevice(PcmFormatInfo format, ushort sdlFormat, IntPtr callbackPtr, GCHandle userdataGcHandle, out SDLAudioSpec obtained)
    {
        var desired = new SDLAudioSpec
        {
            Freq = format.SampleRate,
            Format = sdlFormat,
            Channels = checked((byte)format.Channels),
            Samples = 1024,
            Callback = (void*)callbackPtr,
            Userdata = GCHandle.ToIntPtr(userdataGcHandle).ToPointer()
        };

        fixed (SDLAudioSpec* obtainedPtr = &obtained)
        {
            var device = SDL.OpenAudioDevice((byte*)null, 0, &desired, obtainedPtr, 0);
            return device;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Do not block the UI while a callback is finishing. New plays are
        // rejected immediately; SDL is released after active device cleanup.
        _ = DisposeWhenIdleAsync();
    }

    private async Task DisposeWhenIdleAsync()
    {
        await _playbackGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (InitLock)
            {
                if (s_instanceCount > 0 && --s_instanceCount == 0)
                {
                    SDL.QuitSubSystem(SDL_INIT_AUDIO);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SDL shutdown failed: {ex}");
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private static ushort GetSdlFormat(PcmSampleFormat format)
    {
        return format switch
        {
            PcmSampleFormat.U8 => AUDIO_U8,
            PcmSampleFormat.S16LE => AUDIO_S16LSB,
            _ => 0
        };
    }

    private static bool SdlFormatIsUnsigned(ushort sdlFormat)
    {
        return (sdlFormat & (1 << 15)) == 0;
    }

    private class AudioCallbackData
    {
        public required Stream PcmStream { get; set; }
        public ushort SdlFormat { get; set; }
        public bool FinalBufferQueued { get; set; }
        public byte[] MixBuffer { get; set; } = [];
        public required TaskCompletionSource Tcs { get; set; }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SdlPcmPlayer));
        }
    }
}
