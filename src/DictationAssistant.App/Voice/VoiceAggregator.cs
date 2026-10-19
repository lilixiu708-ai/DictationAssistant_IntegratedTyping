using DictationAssistant.App.Abstractions;
using System.Diagnostics;

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
        ct.ThrowIfCancellationRequested();
        var all = new List<IVoiceFactory>();
        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var factories = await provider.GetFactoriesAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                all.AddRange(factories);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ct.ThrowIfCancellationRequested();
                Trace.WriteLine($"Voice provider {provider.GetType().Name} failed: {ex}");
            }
        }
        return all;
    }
}
