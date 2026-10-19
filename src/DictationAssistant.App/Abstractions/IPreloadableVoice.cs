using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;

namespace DictationAssistant.App.Abstractions;

public interface IPreloadableVoice : IVoice
{
    Task PreloadAsync(string text, VoiceSynthesisOptions options, CancellationToken ct);
}
