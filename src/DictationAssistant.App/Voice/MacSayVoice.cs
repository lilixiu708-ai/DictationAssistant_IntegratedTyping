using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;

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
        return BassDecoder.FromStream(new MemoryStream(aiffBytes));
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
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                var info = new VoiceInfo
                {
                    Id = $"say:{tokens[0]}",
                    DisplayName = tokens[0],
                    LocaleOrLanguage = tokens.Length > 1 ? tokens[1] : null,
                };
                voices.Add(new MacSayVoiceFactory(info));
            }

            return voices;
        }
        catch
        {
            return [];
        }
    }
}
