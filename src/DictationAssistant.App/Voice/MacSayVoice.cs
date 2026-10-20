using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;
using System.Text.RegularExpressions;

namespace DictationAssistant.App.Voice;

public sealed class MacSayVoice : IVoice
{
    private readonly string _voiceName;

    public MacSayVoice(string voiceName)
    {
        _voiceName = voiceName;
    }

    public async Task<PcmAudio> SynthesizePcmAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        var aiffBytes = await SynthesizeAiffAsync(text, options, ct).ConfigureAwait(false);
        if (aiffBytes is null)
        {
            return PcmAudio.Empty;
        }
        return OpenAudioDecoder.FromStream(new MemoryStream(aiffBytes));
    }

    private async Task<byte[]?> SynthesizeAiffAsync(string text, VoiceSynthesisOptions options, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dictationassistant-say-{Guid.NewGuid():N}.aiff");
        try
        {
            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(_voiceName))
            {
                args.Add("-v");
                args.Add(_voiceName);
            }

            if (options.Rate is int rate)
            {
                args.Add("-r");
                args.Add((175 + Math.Clamp(rate, -10, 10) * 15).ToString());
            }

            args.Add("-o");
            args.Add(tempFile);
            args.Add("--file-format=AIFF");
            args.Add("-f");
            args.Add("-");

            await ProcessRunner.RunAsync("say", args, text, cancellationToken).ConfigureAwait(false);
            return await File.ReadAllBytesAsync(tempFile, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return null;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}

public sealed class MacSayVoiceFactory : IVoiceFactory
{
    private readonly VoiceInfo _info;

    public MacSayVoiceFactory(VoiceInfo info)
    {
        _info = info;
    }

    public VoiceInfo Info => _info;

    public IVoice Create()
    {
        return new MacSayVoice(_info.DisplayName);
    }

    public override string ToString() => _info.DisplayName;
}

public sealed class MacSayVoiceFactoryProvider : IVoiceFactoryProvider
{
    private static readonly Regex VoiceLine = new(
        @"^(?<name>.+?)\s+(?<locale>[A-Za-z]{2,3}(?:[_-][A-Za-z0-9]+)+)\s+#",
        RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<IVoiceFactory>> GetFactoriesAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return [];
        }
        try
        {
            var output = await ProcessRunner.RunCaptureAsync("say", ["-v", "?"], null, cancellationToken).ConfigureAwait(false);
            var voices = new List<IVoiceFactory>();
            using var reader = new StringReader(output);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (ParseVoiceLine(line) is { } info)
                    voices.Add(new MacSayVoiceFactory(info));
            }

            return voices;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return [];
        }
    }

    private static VoiceInfo? ParseVoiceLine(string line)
    {
        // Newer macOS voice names can include spaces and parenthesized language
        // names. The locale column, followed by '#', marks the end of the name.
        var match = VoiceLine.Match(line.Trim());
        if (!match.Success) return null;
        var name = match.Groups["name"].Value.Trim();
        return new VoiceInfo
        {
            Id = $"say:{name}",
            DisplayName = name,
            LocaleOrLanguage = match.Groups["locale"].Value,
        };
    }
}
