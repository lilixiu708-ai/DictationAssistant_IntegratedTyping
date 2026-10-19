using System.Diagnostics;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;

namespace DictationAssistant.App.Voice;

public sealed class ImprovedVoice : IVoice, IPreloadableVoice
{
    private static readonly string[] AudioFileExtensions = ["wav", "flac", "ape", "m4a", "opus", "aac", "mp3", "mp2", "mp1", "ogg", "wma", "aif", "mp4"];

    private readonly IVoice _inner;
    private readonly string _resourceDirectory;

    public ImprovedVoice(IVoice inner, string resourceDirectory)
    {
        _inner = inner;
        _resourceDirectory = resourceDirectory;
    }

    public Task PreloadAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        if (_inner is IPreloadableVoice preloadableInner)
        {
            if (FindFile(text) is null)
            {
                return preloadableInner.PreloadAsync(text, options, ct);
            }
        }
        return Task.CompletedTask;
    }

    public async Task<PcmAudio> SynthesizePcmAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        var filePath = FindFile(text);
        if (filePath is not null)
        {
            try
            {
                return BassDecoder.FromFile(filePath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImprovedVoice] Failed to load audio from file '{filePath}': {ex.Message}");
            }
        }

        return await _inner.SynthesizePcmAsync(text, options, ct).ConfigureAwait(false);
    }

    private string? FindFile(string text)
    {
        var sanitized = text
            .Replace("\\", "")
            .Replace("/", "")
            .Replace(":", "")
            .Replace("*", "")
            .Replace("?", "")
            .Replace("\"", "")
            .Replace("<", "")
            .Replace(">", "")
            .Replace("|", "");

        var basePath = Path.Combine(_resourceDirectory, sanitized);

        foreach (var ext in AudioFileExtensions)
        {
            var fullPath = basePath + "." + ext;
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}
