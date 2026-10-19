using System.Diagnostics;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Helpers;
using DictationAssistant.App.Models;
using EdgeTTS.DotNet;
using EdgeTTS.DotNet.Models;

namespace DictationAssistant.App.Voice;

public sealed class EdgeTtsVoice : IPreloadableVoice
{
    private readonly string _voiceName;
    private readonly CachedDataLoader<CacheKey, byte[]> _cache;

    public EdgeTtsVoice(string voiceName, int cacheCapacity = 4)
    {
        _voiceName = voiceName;
        _cache = new CachedDataLoader<CacheKey, byte[]>(LoadMp3Async, cacheCapacity);
    }

    public async Task<PcmAudio> SynthesizePcmAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text))
        {
            return PcmAudio.Empty;
        }

        var key = new CacheKey(text, options.Rate ?? 0);

        try
        {
            var mp3Bytes = await _cache.GetAsync(key, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return DecodeMp3ToPcm(mp3Bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[EdgeTTS] Synthesis failed: {ex.Message}");
            return PcmAudio.Empty;
        }
    }

    public async Task PreloadAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var key = new CacheKey(text, options.Rate ?? 0);

        // The caller observes failure and controls its own wait; the shared
        // cache load remains available to other callers after cancellation.
        await _cache.GetAsync(key, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> LoadMp3Async(CacheKey key)
    {
        var rate = MapRate(key.Rate);
        var communicate = new Communicate(key.Text, voice: _voiceName, rate: rate);

        var mp3Bytes = new List<byte>();

        await foreach (var chunk in communicate.StreamAsync())
        {
            if (chunk is AudioChunk audio)
            {
                mp3Bytes.AddRange(audio.Data);
            }
        }

        if (mp3Bytes.Count == 0)
        {
            Trace.WriteLine("[EdgeTTS] No audio data received");
            throw new InvalidOperationException("No audio data received from EdgeTTS");
        }

        return mp3Bytes.ToArray();
    }

    private static string MapRate(int rate)
    {
        var edgeRate = rate * 10;
        return edgeRate >= 0 ? $"+{edgeRate}%" : $"{edgeRate}%";
    }

    private static PcmAudio DecodeMp3ToPcm(byte[] mp3Bytes)
    {
        return BassDecoder.FromStream(new MemoryStream(mp3Bytes));
    }

    private readonly record struct CacheKey(string Text, int Rate);
}

public sealed class EdgeTtsVoiceFactory : IVoiceFactory
{
    private readonly VoiceInfo _info;
    private readonly string _internalName;

    public EdgeTtsVoiceFactory(VoiceInfo info, string internalName)
    {
        _info = info;
        _internalName = internalName;
    }

    public VoiceInfo Info => _info;

    public IVoice Create()
    {
        return new EdgeTtsVoice(_internalName);
    }

    public override string ToString() => _info.DisplayName;
}

public sealed class EdgeTtsVoiceFactoryProvider : IVoiceFactoryProvider
{
    public async Task<IReadOnlyList<IVoiceFactory>> GetFactoriesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Task? request = null;
        try
        {
            var voiceListTask = Voices.ListVoicesAsync();
            request = voiceListTask;
            var voices = await voiceListTask.WaitAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return voices.Select(v => new EdgeTtsVoiceFactory(new VoiceInfo
            {
                Id = $"edge-tts:{v.ShortName}",
                DisplayName = v.FriendlyName ?? v.ShortName,
                LocaleOrLanguage = v.Locale,
            }, v.ShortName)).ToList();
        }
        catch (OperationCanceledException)
        {
            // EdgeTTS does not accept our token. Stop waiting during shutdown,
            // but still observe a later failure of the read-only HTTP request.
            if (request is not null) _ = ObserveCancelledRequestAsync(request);
            throw;
        }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested();
            Trace.WriteLine($"[EdgeTTS] Failed to list voices: {ex.Message}");
            return [];
        }
    }

    private static async Task ObserveCancelledRequestAsync(Task request)
    {
        try { await request.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Trace.WriteLine($"[EdgeTTS] Voice list request finished after cancellation: {ex.Message}"); }
    }
}
