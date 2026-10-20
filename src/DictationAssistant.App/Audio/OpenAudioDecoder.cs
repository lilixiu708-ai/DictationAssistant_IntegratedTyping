// Modified 2026-10-20: replace the proprietary BASS chain with MIT-licensed decoders.
// SPDX-License-Identifier: AGPL-3.0-or-later
using NAudio.Wave;
using NAudio.Dmo;
using NLayer.NAudioSupport;
using System.Buffers.Binary;

namespace DictationAssistant.App.Audio;

/// <summary>Streams WAV, AIFF and MPEG audio; other Windows formats use installed system codecs.</summary>
public static class OpenAudioDecoder
{
    private const long MaximumBufferedInputBytes = 64L * 1024 * 1024;

    public static PcmAudio FromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var path = Path.GetFullPath(filePath);
        try
        {
            return FromStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.SequentialScan));
        }
        catch (NotSupportedException) when (OperatingSystem.IsWindows())
        {
            // Media Foundation is part of Windows. No codec binary is bundled with the application.
            WaveStream? reader = null;
            try
            {
                reader = new MediaFoundationReader(path);
                return CreateAudio(reader, null, signedEightBit: false);
            }
            catch
            {
                reader?.Dispose();
                throw;
            }
        }
    }

    /// <summary>Reads from the current position; disposes the input on completion or failure unless leaveOpen is true.</summary>
    public static PcmAudio FromStream(Stream inputStream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        Stream source = inputStream;
        WaveStream? reader = null;
        try
        {
            source = new DecoderInputStream(inputStream, leaveOpen);
            if (!source.CanRead) throw new ArgumentException("音频输入流不可读取。", nameof(inputStream));
            if (!source.CanSeek) source = BufferToTemporaryFile(source);
            Span<byte> header = stackalloc byte[12];
            var read = 0;
            while (read < header.Length)
            {
                var count = source.Read(header[read..]);
                if (count == 0) break;
                read += count;
            }
            source.Position = 0;
            var isAiff = read >= 12 && header[..4].SequenceEqual("FORM"u8) &&
                (header[8..].SequenceEqual("AIFF"u8) || header[8..].SequenceEqual("AIFC"u8));
            if (read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..].SequenceEqual("WAVE"u8))
                reader = new WaveFileReader(source);
            else if (isAiff)
                reader = new AiffFileReader(source);
            else if (read >= 3 && (header[..3].SequenceEqual("ID3"u8) || IsMpegHeader(header)))
                reader = new Mp3FileReaderBase(source, format => new Mp3FrameDecompressor(format));
            else
                throw new NotSupportedException("此音频流不是受支持的 WAV、AIFF 或 MP3/MPEG 音频。其他文件格式在 Windows 上依赖系统解码器。");

            return CreateAudio(reader, source, isAiff && reader.WaveFormat.BitsPerSample == 8);
        }
        catch
        {
            try { reader?.Dispose(); }
            finally
            {
                if (!ReferenceEquals(source, inputStream) || !leaveOpen) source.Dispose();
            }
            throw;
        }
    }

    private static bool IsMpegHeader(ReadOnlySpan<byte> header) => header[0] == 0xff &&
        (header[1] & 0xe0) == 0xe0 && (header[1] & 0x06) != 0 && (header[1] & 0x18) != 0x08;

    private static PcmAudio CreateAudio(WaveStream reader, Stream? input, bool signedEightBit)
    {
        var format = NormalizeFormat(reader.WaveFormat);
        if (format.SampleRate <= 0 || format.Channels is < 1 or > 8)
            throw new InvalidDataException("音频采样率无效或声道数量不在 1–8 范围内。");
        if ((format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is not (8 or 16 or 24 or 32)) ||
            (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample is not (32 or 64)))
            throw new NotSupportedException($"不支持此 PCM 位深：{format.BitsPerSample}。");
        if (format.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat)
        {
            if (format.BlockAlign != format.Channels * (format.BitsPerSample / 8) ||
                (reader is WaveFileReader or AiffFileReader && reader.Length % format.BlockAlign != 0))
                throw new InvalidDataException("音频数据不是完整的 PCM 帧。");
        }
        var source = new StandardWaveProvider(reader, format, signedEightBit);
        IWaveProvider output = source;
        var sampleFormat = PcmSampleFormat.S16LE;
        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 8)
            sampleFormat = PcmSampleFormat.U8;
        else if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16)
        {
            if (format.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat))
                throw new NotSupportedException($"不支持此 WAV 编码：{format.Encoding}。");
            // NLayer outputs float PCM; WAV may also contain 24/32-bit PCM or IEEE float.
            // NAudio converts one bounded block at a time instead of materializing the decoded file.
            output = source.ToSampleProvider().ToWaveProvider16();
        }
        return new PcmAudio
        {
            Data = new DecodedPcmStream(output, reader, input),
            Format = new PcmFormatInfo(output.WaveFormat.SampleRate, output.WaveFormat.Channels, sampleFormat)
        };
    }

    private static WaveFormat NormalizeFormat(WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Extensible) return format;
        Guid subFormat;
        if (format is WaveFormatExtensible extensible)
            subFormat = extensible.SubFormat;
        else if (format is WaveFormatExtraData extra && extra.ExtraSize >= 22)
        {
            // WaveFileReader returns WaveFormatExtraData for extensible WAV headers;
            // AsStandardWaveFormat only recognizes the separate WaveFormatExtensible type.
            var validBits = BinaryPrimitives.ReadUInt16LittleEndian(extra.ExtraData);
            if (validBits > format.BitsPerSample)
                throw new InvalidDataException("WAV 有效位数超过了容器位深。");
            subFormat = new Guid(extra.ExtraData.AsSpan(6, 16));
        }
        else
            throw new InvalidDataException("WAV 扩展格式头不完整。");
        var encoding = subFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM ? WaveFormatEncoding.Pcm :
            subFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT ? WaveFormatEncoding.IeeeFloat :
            throw new NotSupportedException($"不支持此 WAV 扩展编码：{subFormat}。");
        return WaveFormat.CreateCustomFormat(encoding, format.SampleRate, format.Channels,
            format.AverageBytesPerSecond, format.BlockAlign, format.BitsPerSample);
    }

    private static Stream BufferToTemporaryFile(Stream source)
    {
        // Seek is required by container headers and MPEG frame indexing. Spool non-seekable
        // input to bounded temporary storage; do not keep a second whole encoded file in memory.
        var path = Path.Combine(Path.GetTempPath(), $"dictation-audio-{Guid.NewGuid():N}.tmp");
        FileStream? temporary = null;
        try
        {
            temporary = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                16 * 1024, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            var buffer = new byte[16 * 1024];
            long total = 0;
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > MaximumBufferedInputBytes)
                    throw new InvalidDataException("不可定位的音频输入不能超过 64 MiB。");
                temporary.Write(buffer, 0, read);
            }
            source.Dispose();
            temporary.Position = 0;
            return temporary;
        }
        catch
        {
            temporary?.Dispose();
            throw;
        }
    }

    private sealed class StandardWaveProvider(WaveStream reader, WaveFormat format, bool signedEightBit) : IWaveProvider
    {
        public WaveFormat WaveFormat => format;
        public int Read(byte[] buffer, int offset, int count)
        {
            var read = reader.Read(buffer, offset, count);
            // AIFF stores signed 8-bit samples, while WAV/SDL U8 uses a midpoint of 128.
            if (signedEightBit)
                for (var index = offset; index < offset + read; index++) buffer[index] ^= 0x80;
            return read;
        }
    }

    /// <summary>Gives readers their own origin while retaining explicit ownership of the caller's stream.</summary>
    private sealed class DecoderInputStream(Stream inner, bool leaveOpen) : Stream
    {
        private readonly long _origin = inner.CanSeek ? inner.Position : 0;
        private bool _disposed;
        public override bool CanRead => !_disposed && inner.CanRead;
        public override bool CanSeek => !_disposed && inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length - _origin;
        public override long Position
        {
            get => inner.Position - _origin;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                inner.Position = checked(_origin + value);
            }
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => inner.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        public override int ReadByte() => inner.ReadByte();
        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(Position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return Position;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing && !leaveOpen) inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
