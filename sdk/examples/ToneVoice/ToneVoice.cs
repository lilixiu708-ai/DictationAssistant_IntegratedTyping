// PCM voice-provider example. Generates a tone, not speech. AGPL-3.0-or-later.
using System.Buffers.Binary;
using DictationAssistant.Plugin.Abstractions;

namespace Example.ToneVoice;

public sealed class ToneVoice : IVoiceProviderPlugin
{
    public const string VoiceId = "tone-440";
    private const int SampleRate = 24_000;
    private static readonly IReadOnlyList<PluginVoiceInfo> Voices = Array.AsReadOnly(new[]
    {
        new PluginVoiceInfo(VoiceId, "440 Hz 离线提示音（不朗读文本）")
    });

    public Task<IReadOnlyList<PluginVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Voices);
    }

    public Task<PluginAudioData> SynthesizeAsync(string voiceId, string text, PluginSpeechOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (voiceId != VoiceId) throw new ArgumentException("未知的提示音。", nameof(voiceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(options);

        // The text controls duration only. Keep this demonstration under 2 seconds.
        var speed = 1 + Math.Clamp(options.Rate, -10, 10) * 0.05;
        var duration = Math.Clamp(text.Length * 0.04 / speed, 0.1, 2.0);
        var samples = (int)(SampleRate * duration);
        var data = new byte[samples * sizeof(short)];
        var fadeSamples = SampleRate / 200;
        for (var index = 0; index < samples; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var envelope = Math.Min(1.0, Math.Min(index, samples - 1 - index) / (double)fadeSamples);
            var value = (short)Math.Round(Math.Sin(2 * Math.PI * 440 * index / SampleRate) *
                short.MaxValue * 0.15 * envelope);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * sizeof(short), sizeof(short)), value);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PluginAudioData(data, SampleRate, 1));
    }
}
