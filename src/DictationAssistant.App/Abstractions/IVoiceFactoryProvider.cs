using DictationAssistant.App.Abstractions;

namespace DictationAssistant.App.Abstractions;

public interface IVoiceFactoryProvider
{
    Task<IReadOnlyList<IVoiceFactory>> GetFactoriesAsync(CancellationToken ct);
}
