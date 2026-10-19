namespace DictationAssistant.Plugin.Abstractions;

/// <summary>A voice provider does not open an audio device or access UI controls.</summary>
public interface IVoiceProviderPlugin
{
    Task<IReadOnlyList<PluginVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken);
    Task<PluginAudioData> SynthesizeAsync(string voiceId, string text, PluginSpeechOptions options,
        CancellationToken cancellationToken);
}

public sealed record PluginVoiceInfo(string Id, string Name, string? Locale = null);
public sealed record PluginSpeechOptions(int Rate = 0);

/// <summary>Return a complete PCM buffer. Do not mutate a buffer after returning it.</summary>
public interface IAudioEffectPlugin
{
    Task<PluginAudioData> ProcessAsync(PluginAudioData audio, CancellationToken cancellationToken);
}

public enum PluginPcmSampleFormat
{
    U8,
    S16LE
}

/// <summary>Uncompressed PCM, limited by the host to 64 MiB per result. No plugin stream runs in a native callback.</summary>
public sealed record PluginAudioData(byte[] Data, int SampleRate, int Channels,
    PluginPcmSampleFormat SampleFormat = PluginPcmSampleFormat.S16LE);
