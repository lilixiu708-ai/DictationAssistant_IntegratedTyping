using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;

namespace DictationAssistant.App.Services;

public sealed class NullVoice : IVoice
{
    public static NullVoice Instance { get; } = new();

    public Task<PcmAudio> SynthesizePcmAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        _ = text;
        _ = options;
        _ = ct;
        return Task.FromResult(PcmAudio.Empty);
    }
}
