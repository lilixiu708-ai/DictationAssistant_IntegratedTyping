using DictationAssistant.App.Abstractions;

namespace DictationAssistant.App.Voice;

public sealed class VoiceAggregator
{
    private readonly IVoiceFactoryProvider[] _providers;

    public VoiceAggregator(params IVoiceFactoryProvider[] providers)
    {
        _providers = providers;
    }

    public async Task<IReadOnlyList<IVoiceFactory>> GetAllFactoriesAsync(CancellationToken ct)
    {
        var all = new List<IVoiceFactory>();
        foreach (var provider in _providers)
        {
            try
            {
                var factories = await provider.GetFactoriesAsync(ct).ConfigureAwait(false);
                all.AddRange(factories);
            }
            catch
            {
            }
        }
        return all;
    }
}
