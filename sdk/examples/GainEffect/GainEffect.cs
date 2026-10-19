// Whole-buffer PCM gain example. AGPL-3.0-or-later.
using System.Buffers.Binary;
using DictationAssistant.Plugin.Abstractions;

namespace Example.GainEffect;

public sealed class GainEffect : IAudioEffectPlugin
{
    // Half amplitude. Clamp each sample if experimenting with gains above 1.
    private const double Gain = 0.5;

    public Task<PluginAudioData> ProcessAsync(PluginAudioData audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        cancellationToken.ThrowIfCancellationRequested();
        if (audio.Data is null || audio.Data.Length > 64 * 1024 * 1024 ||
            audio.SampleRate <= 0 || audio.Channels is < 1 or > 255)
            throw new InvalidDataException("无效的 PCM 缓冲区或格式。");
        var bytesPerSample = audio.SampleFormat switch
        {
            PluginPcmSampleFormat.U8 => 1,
            PluginPcmSampleFormat.S16LE => 2,
            _ => throw new NotSupportedException("本示例仅处理 U8 和 S16LE PCM。")
        };
        if (audio.Data.Length % (bytesPerSample * audio.Channels) != 0)
            throw new InvalidDataException("PCM 缓冲区没有完整的采样帧。");

        var output = new byte[audio.Data.Length];
        for (var offset = 0; offset < output.Length; offset += bytesPerSample)
        {
            if ((offset & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (bytesPerSample == 1)
            {
                var sample = (audio.Data[offset] - 128) * Gain + 128;
                output[offset] = (byte)Math.Clamp(Math.Round(sample), byte.MinValue, byte.MaxValue);
            }
            else
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(audio.Data.AsSpan(offset, sizeof(short)));
                var gained = (short)Math.Clamp(Math.Round(sample * Gain), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset, sizeof(short)), gained);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Retain format metadata; never modify the input or retain the returned buffer.
        return Task.FromResult(audio with { Data = output });
    }
}
