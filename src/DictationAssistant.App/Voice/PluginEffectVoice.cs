using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;
using DictationAssistant.App.Plugins;

namespace DictationAssistant.App.Voice;

/// <summary>Buffers PCM only when an initialized effect needs a complete audio block.</summary>
internal sealed class PluginEffectVoice : IPreloadableVoice
{
    private readonly IVoice _inner;
    private readonly PluginRuntime _runtime;

    public PluginEffectVoice(IVoice inner, PluginRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(runtime);
        _inner = inner;
        _runtime = runtime;
    }

    public Task PreloadAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _inner is IPreloadableVoice preloadable
            ? preloadable.PreloadAsync(text, options, ct)
            : Task.CompletedTask;
    }

    public Task<PcmAudio> SynthesizePcmAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var applyEffects = PluginAudioBridge.HasEffects(_runtime);
        // Local synthesis and decoder ReadAsync may both complete synchronously.
        return applyEffects
            ? Task.Run(() => SynthesizeCoreAsync(text, options, applyEffects, ct), ct)
            : SynthesizeCoreAsync(text, options, applyEffects, ct);
    }

    private async Task<PcmAudio> SynthesizeCoreAsync(string text, VoiceSynthesisOptions options,
        bool applyEffects, CancellationToken ct)
    {
        PcmAudio? source = await _inner.SynthesizePcmAsync(text, options, ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!applyEffects)
            {
                var original = source;
                source = null;
                return original;
            }

            var consumed = source;
            var audio = await PluginAudioBridge.FromPcmAsync(consumed, ct).ConfigureAwait(false);
            source = null;
            consumed.Dispose();
            audio = await PluginAudioBridge.ProcessAsync(_runtime, audio, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return PluginAudioBridge.ToPcm(audio);
        }
        finally
        {
            source?.Dispose();
        }
    }
}
