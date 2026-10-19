using DictationAssistant.App.Audio;
using Hexa.NET.SDL2;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DictationAssistant.App.Audio;

public class PcmWriter : IDisposable
{
    private static readonly byte[] Byte00_1M = new byte[1048576];
    private static readonly byte[] Byte80_1M = new byte[1048576];

    static PcmWriter()
    {
        Array.Fill(Byte80_1M, (byte)0x80);
    }

    private readonly PcmAudio _dest;
    private readonly bool _leaveOpen;

    public PcmWriter(PcmAudio dest, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(dest);
        ValidateFormat(dest.Format);
        if (!dest.Data.CanWrite)
            throw new ArgumentException("PCM destination must be writable.", nameof(dest));
        _dest = dest;
        _leaveOpen = leaveOpen;
    }

    public long MillisecondsToSamples(long ms)
    {
        return checked(((ms / 1000) * _dest.Format.SampleRate) + (((ms % 1000) * _dest.Format.SampleRate) / 1000));
    }

    public long MillisecondsToBytes(long ms)
    {
        return checked(MillisecondsToSamples(ms) * GetBlockAlign());
    }

    public long BytesToMilliseconds(long byteOffset)
    {
        var blockAlign = GetBlockAlign();
        var samples = byteOffset / blockAlign;
        return (samples / _dest.Format.SampleRate) * 1000 + ((samples % _dest.Format.SampleRate) * 1000) / _dest.Format.SampleRate;
    }

    private int GetBlockAlign()
    {
        var bytesPerSample = _dest.Format.SampleFormat == PcmSampleFormat.U8 ? 1 : 2;
        return _dest.Format.Channels * bytesPerSample;
    }

    public long WriteDelay(long ms)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ms);
        var len = MillisecondsToBytes(ms);
        var remainingBytes = len;

        var emptyData = _dest.Format.SampleFormat == PcmSampleFormat.U8 ? Byte80_1M : Byte00_1M;

        while (remainingBytes > 0)
        {
            var bytesToWrite = (int)Math.Min(remainingBytes, emptyData.Length);
            _dest.Data.Write(emptyData, 0, bytesToWrite);
            remainingBytes -= bytesToWrite;
        }

        return len;
    }

    public unsafe long Write(PcmAudio input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateFormat(input.Format);
        if (!input.Data.CanRead)
            throw new ArgumentException("PCM input must be readable.", nameof(input));
        var srcFormat = GetSdlFormat(input.Format.SampleFormat);
        var dstFormat = GetSdlFormat(_dest.Format.SampleFormat);
        var srcChannels = (byte)input.Format.Channels;
        var srcSampleRate = input.Format.SampleRate;
        var dstChannels = (byte)_dest.Format.Channels;
        var dstSampleRate = _dest.Format.SampleRate;

        SDLAudioCVT cvt = new SDLAudioCVT();
        var conversion = SDL.BuildAudioCVT(ref cvt, srcFormat, srcChannels, srcSampleRate, dstFormat, dstChannels, dstSampleRate);
        if (conversion < 0)
            throw new InvalidOperationException($"SDL audio conversion setup failed: {SDL.GetErrorS()}");
        if (cvt.LenMult < 1)
            throw new InvalidOperationException("SDL audio conversion returned an invalid buffer multiplier.");

        var bytesPerSample = input.Format.SampleFormat == PcmSampleFormat.U8 ? 1 : 2;
        var srcBlockAlign = srcChannels * bytesPerSample;
        var len = 1024 * srcBlockAlign;
        byte[] buf = new byte[checked(len * cvt.LenMult)];
        GCHandle p = GCHandle.Alloc(buf, GCHandleType.Pinned);

        long totalBytes = 0;
        try
        {
            cvt.Buf = (byte*)p.AddrOfPinnedObject();
            do
            {
                int numOfRead = 0;
                while (numOfRead < len)
                {
                    var t = input.Data.Read(buf, numOfRead, len - numOfRead);
                    if ((uint)t > (uint)(len - numOfRead))
                        throw new InvalidDataException("PCM stream returned an invalid read count.");
                    numOfRead += t;
                    if (t == 0)
                        break;
                }

                if (numOfRead == 0)
                    break;
                if (numOfRead % srcBlockAlign != 0)
                    throw new InvalidDataException("PCM input ended with an incomplete sample frame.");
                var convertedLength = numOfRead;
                if (conversion != 0)
                {
                    cvt.Len = numOfRead;
                    if (SDL.ConvertAudio(ref cvt) < 0)
                        throw new InvalidOperationException($"SDL audio conversion failed: {SDL.GetErrorS()}");
                    convertedLength = cvt.LenCvt;
                }
                if ((uint)convertedLength > (uint)buf.Length)
                    throw new InvalidOperationException("SDL audio conversion returned an invalid output size.");
                _dest.Data.Write(buf, 0, convertedLength);
                totalBytes += convertedLength;
            }
            while (true);
        }
        finally
        {
            p.Free();
        }

        var dstBlockAlign = GetBlockAlign();
        if (totalBytes % dstBlockAlign != 0)
        {
            int lenAlign = (int)(dstBlockAlign - (totalBytes % dstBlockAlign));
            byte[] bufAlign = new byte[lenAlign];
            _dest.Data.Write(bufAlign, 0, lenAlign);
            totalBytes += lenAlign;
        }

        return totalBytes;
    }

    private static void ValidateFormat(PcmFormatInfo format)
    {
        if (format.SampleRate <= 0 || format.Channels is < 1 or > byte.MaxValue)
            throw new ArgumentException("PCM sample rate and channel count are invalid.", nameof(format));
        _ = GetSdlFormat(format.SampleFormat);
    }

    private static ushort GetSdlFormat(PcmSampleFormat format)
    {
        return format switch
        {
            PcmSampleFormat.U8 => 0x0008,
            PcmSampleFormat.S16LE => 0x8010,
            _ => throw new NotSupportedException($"Unsupported format: {format}")
        };
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_leaveOpen)
            {
                _dest.Data.Close();
            }
            else
            {
                _dest.Data.Flush();
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }
}
