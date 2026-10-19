using DictationAssistant.App.Models;

namespace DictationAssistant.App.Abstractions;

public interface IVoiceFactory
{
    VoiceInfo Info { get; }
    IVoice Create();
}
