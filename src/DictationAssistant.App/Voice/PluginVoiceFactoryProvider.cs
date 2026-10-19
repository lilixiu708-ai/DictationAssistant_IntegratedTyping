using System.Diagnostics;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;
using DictationAssistant.App.Plugins;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Voice;

internal sealed class PluginVoiceFactoryProvider : IVoiceFactoryProvider
{
    private readonly PluginRuntime _runtime;

    public PluginVoiceFactoryProvider(PluginRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    public async Task<IReadOnlyList<IVoiceFactory>> GetFactoriesAsync(CancellationToken ct)
    {
        await _runtime.InitializeAsync(ct).ConfigureAwait(false);
        var factories = new List<IVoiceFactory>();
        foreach (var entry in _runtime.Catalog.Entries
                     .Where(entry => entry.IsLoaded && entry.IsInitialized && entry.VoiceProvider is not null)
                     .OrderBy(entry => entry.Manifest!.Id, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var voices = await _runtime.InvokeAsync(entry, "获取语音列表", async token =>
                {
                    var voices = await entry.VoiceProvider!.GetVoicesAsync(token).ConfigureAwait(false);
                    if (voices is null || voices.Count > 4096)
                        throw new InvalidDataException("语音插件返回了无效或超过 4096 条的语音列表。");
                    var snapshot = Array.AsReadOnly(voices.ToArray());
                    if (snapshot.Count > 4096)
                        throw new InvalidDataException("语音插件返回了超过 4096 条的语音列表。");
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var voice in snapshot)
                    {
                        token.ThrowIfCancellationRequested();
                        if (voice is null || !ValidLabel(voice.Id, 200) || !ValidLabel(voice.Name, 200) ||
                            voice.Locale?.Length > 100 || voice.Locale?.Any(char.IsControl) == true || !ids.Add(voice.Id))
                            throw new InvalidDataException("语音插件返回了无效或重复的语音标识。");
                    }
                    return snapshot;
                }, ct, waitForTurn: false).ConfigureAwait(false);
                foreach (var voice in voices)
                    factories.Add(new PluginVoiceFactory(_runtime, entry, voice));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ct.ThrowIfCancellationRequested();
                Trace.WriteLine($"[PluginVoice] {entry.DisplayName}: {ex}");
            }
        }
        return factories;
    }

    private static bool ValidLabel(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);

    private sealed class PluginVoiceFactory : IVoiceFactory
    {
        private readonly PluginRuntime _runtime;
        private readonly PluginEntry _entry;
        private readonly string _voiceId;

        public PluginVoiceFactory(PluginRuntime runtime, PluginEntry entry, PluginVoiceInfo voice)
        {
            _runtime = runtime;
            _entry = entry;
            _voiceId = voice.Id;
            Info = new VoiceInfo
            {
                Id = $"plugin:{entry.Manifest!.Id}:{voice.Id}",
                DisplayName = $"{entry.DisplayName} · {voice.Name}",
                LocaleOrLanguage = voice.Locale
            };
        }

        public VoiceInfo Info { get; }
        public IVoice Create() => new PluginVoice(_runtime, _entry, _voiceId);
        public override string ToString() => Info.DisplayName;
    }

    private sealed class PluginVoice : IVoice
    {
        private readonly PluginRuntime _runtime;
        private readonly PluginEntry _entry;
        private readonly string _voiceId;

        public PluginVoice(PluginRuntime runtime, PluginEntry entry, string voiceId)
        {
            _runtime = runtime;
            _entry = entry;
            _voiceId = voiceId;
        }

        public async Task<PcmAudio> SynthesizePcmAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) return PcmAudio.Empty;
            ArgumentNullException.ThrowIfNull(options);
            var speechOptions = new PluginSpeechOptions(Math.Clamp(options.Rate ?? 0, -10, 10));
            var audio = await _runtime.InvokeAsync(_entry, "合成语音", async token =>
            {
                var result = await _entry.VoiceProvider!.SynthesizeAsync(_voiceId, text, speechOptions, token)
                    .ConfigureAwait(false);
                PluginAudioBridge.Validate(result);
                return result;
            }, ct, waitForTurn: false).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return PluginAudioBridge.ToPcm(audio);
        }
    }
}
