using System.Buffers;
using System.Buffers.Binary;
using DictationAssistant.App.Plugins;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Audio;

internal static class PluginAudioBridge
{
    internal const int MaximumAudioBytes = 64 * 1024 * 1024;

    public static void Validate(PluginAudioData audio)
    {
        if (audio is null || audio.Data is null)
            throw new InvalidDataException("插件未返回有效的 PCM 音频。");
        if (audio.Data.Length > MaximumAudioBytes)
            throw new InvalidDataException("插件音频不能超过 64 MiB。");
        if (audio.SampleRate is < 8000 or > 192000 || audio.Channels is < 1 or > 8)
            throw new InvalidDataException("插件音频需要 8000–192000 Hz 采样率和 1–8 个声道。");

        var bytesPerSample = audio.SampleFormat switch
        {
            PluginPcmSampleFormat.U8 => 1,
            PluginPcmSampleFormat.S16LE => 2,
            _ => throw new InvalidDataException("插件音频仅支持 U8 或 S16LE PCM。")
        };
        if (audio.Data.Length % (audio.Channels * bytesPerSample) != 0)
            throw new InvalidDataException("插件音频必须包含完整的 PCM 帧。");
    }

    public static PcmAudio ToPcm(PluginAudioData audio)
    {
        Validate(audio);
        return new PcmAudio
        {
            Data = new MemoryStream(audio.Data, writable: false),
            Format = new PcmFormatInfo(audio.SampleRate, audio.Channels,
                audio.SampleFormat == PluginPcmSampleFormat.U8 ? PcmSampleFormat.U8 : PcmSampleFormat.S16LE)
        };
    }

    /// <summary>Reads from the current position without taking ownership of the source.</summary>
    public static async Task<PluginAudioData> FromPcmAsync(PcmAudio pcm, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        ct.ThrowIfCancellationRequested();
        var sampleFormat = pcm.Format.SampleFormat switch
        {
            PcmSampleFormat.U8 => PluginPcmSampleFormat.U8,
            PcmSampleFormat.S16LE => PluginPcmSampleFormat.S16LE,
            _ => throw new InvalidDataException("音频仅支持 U8 或 S16LE PCM。")
        };
        var metadata = new PluginAudioData([], pcm.Format.SampleRate, pcm.Format.Channels, sampleFormat);
        Validate(metadata);
        if (pcm.Data is null || !pcm.Data.CanRead)
            throw new InvalidDataException("PCM 音频流不可读取。");

        byte[] data;
        if (pcm.Data.CanSeek)
        {
            var remaining = pcm.Data.Length - pcm.Data.Position;
            if (remaining is < 0 or > MaximumAudioBytes)
                throw new InvalidDataException("音频不能超过 64 MiB。");
            data = new byte[(int)remaining];
            await pcm.Data.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        }
        else
        {
            using var buffer = new MemoryStream();
            var scratch = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    // Read one additional byte at the limit to distinguish EOF from overflow.
                    var count = (int)Math.Min(scratch.Length, MaximumAudioBytes - buffer.Length + 1);
                    var read = await pcm.Data.ReadAsync(scratch.AsMemory(0, count), ct).ConfigureAwait(false);
                    if (read == 0) break;
                    if (buffer.Length + read > MaximumAudioBytes)
                        throw new InvalidDataException("音频不能超过 64 MiB。");
                    buffer.Write(scratch, 0, read);
                }

                data = buffer.Length == buffer.Capacity ? buffer.GetBuffer() : buffer.ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        ct.ThrowIfCancellationRequested();
        var result = metadata with { Data = data };
        Validate(result);
        return result;
    }

    public static async Task WriteWaveAsync(PluginAudioData audio, Stream destination, CancellationToken ct)
    {
        Validate(audio);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("WAV 目标流不可写入。", nameof(destination));
        ct.ThrowIfCancellationRequested();

        var bytesPerSample = audio.SampleFormat == PluginPcmSampleFormat.U8 ? 1 : 2;
        var blockAlign = audio.Channels * bytesPerSample;
        var padding = audio.Data.Length & 1;
        var header = new byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)(36 + audio.Data.Length + padding));
        "WAVEfmt "u8.CopyTo(header.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), (ushort)audio.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)audio.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), (uint)(audio.SampleRate * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), (ushort)(bytesPerSample * 8));
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), (uint)audio.Data.Length);

        await destination.WriteAsync(header, ct).ConfigureAwait(false);
        await destination.WriteAsync(audio.Data, ct).ConfigureAwait(false);
        if (padding != 0)
            await destination.WriteAsync(new byte[1], ct).ConfigureAwait(false);
    }

    internal static bool HasEffects(PluginRuntime runtime) => runtime.Catalog.Entries.Any(entry =>
        entry.IsLoaded && entry.IsInitialized && entry.AudioEffect is not null);

    public static async Task<PluginAudioData> ProcessAsync(PluginRuntime runtime, PluginAudioData audio,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Validate(audio);
        ct.ThrowIfCancellationRequested();
        var effects = runtime.Catalog.Entries
            .Where(entry => entry.IsLoaded && entry.IsInitialized && entry.AudioEffect is not null)
            .OrderBy(entry => entry.Manifest!.Id, StringComparer.Ordinal);
        foreach (var entry in effects)
        {
            var input = audio;
            audio = await runtime.InvokeAsync(entry, "处理音频", async token =>
            {
                var output = await entry.AudioEffect!.ProcessAsync(input, token).ConfigureAwait(false);
                Validate(output);
                return output;
            }, ct, waitForTurn: false).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        return audio;
    }
}
