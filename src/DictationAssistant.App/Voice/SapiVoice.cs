using System.IO;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DictationAssistant.App.Voice;

public sealed class SapiVoice : IVoice
{
    // https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee125189%28v=vs.85%29
    private const int SAFT48kHz16BitStereo = 39;
    private readonly string _tokenId;

    public SapiVoice(string tokenId)
    {
        _tokenId = tokenId;
    }

    [SupportedOSPlatform("windows")]
    public Task<PcmAudio> SynthesizePcmAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        return Task.Run(() => SynthesizeWindows(text, options, _tokenId), ct);
    }

    [SupportedOSPlatform("windows")]
    private static PcmAudio SynthesizeWindows(string text, VoiceSynthesisOptions options, string tokenId)
    {
        object? voiceObj = null;
        object? streamObj = null;
        object? audioFormatObj = null;

        try
        {
            voiceObj = CreateComObject("SAPI.SpVoice");
            streamObj = CreateComObject("SAPI.SpMemoryStream");
            audioFormatObj = CreateComObject("SAPI.SpAudioFormat");
            if (voiceObj is null || streamObj is null || audioFormatObj is null)
            {
                return PcmAudio.Empty;
            }

            dynamic voice = voiceObj;
            dynamic stream = streamObj;
            dynamic audioFormat = audioFormatObj;

            audioFormat.Type = SAFT48kHz16BitStereo;
            stream.Format = audioFormat;
            voice.AllowAudioOutputFormatChangesOnNextSet = false;
            voice.AudioOutputStream = stream;
            voice.Rate = options.Rate is int rate ? Math.Clamp(rate, -10, 10) : 0;

            var voiceTokenObj = CreateComObject("SAPI.SpObjectToken");
            if (voiceTokenObj is not null)
            {
                try
                {
                    dynamic voiceToken = voiceTokenObj;
                    voiceToken.SetId(tokenId);
                    voice.Voice = voiceToken;
                }
                finally
                {
                    ReleaseCom(voiceTokenObj);
                }
            }

            voice.Speak(text, 0);
            stream.Seek(0, 0);
            var raw = (byte[]?)stream.GetData();
            if (raw is null || raw.Length == 0)
            {
                return PcmAudio.Empty;
            }

            return new PcmAudio
            {
                Data = new MemoryStream(raw),
                Format = new PcmFormatInfo(48000, 2, PcmSampleFormat.S16LE)
            };
        }
        catch
        {
            return PcmAudio.Empty;
        }
        finally
        {
            ReleaseCom(audioFormatObj);
            ReleaseCom(streamObj);
            ReleaseCom(voiceObj);
        }
    }

    [SupportedOSPlatform("windows")]
    private static object? CreateComObject(string progId)
    {
        var comType = Type.GetTypeFromProgID(progId, throwOnError: false);
        return comType is null ? null : Activator.CreateInstance(comType);
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseCom(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}

public sealed class SapiVoiceFactory : IVoiceFactory
{
    private readonly VoiceInfo _info;
    private readonly string _tokenId;

    public SapiVoiceFactory(VoiceInfo info, string tokenId)
    {
        _info = info;
        _tokenId = tokenId;
    }

    public VoiceInfo Info => _info;

    public IVoice Create()
    {
        return new SapiVoice(_tokenId);
    }

    public override string ToString() => _info.DisplayName;
}

public sealed class SapiVoiceFactoryProvider : IVoiceFactoryProvider
{
    private static readonly string[] VoiceCategoryIds =
    [
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech Server\v11.0\Voices",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices"
    ];

    public Task<IReadOnlyList<IVoiceFactory>> GetFactoriesAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyList<IVoiceFactory>>([]);
        }

        ct.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<IVoiceFactory>>(ListVoicesWindows, ct);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<IVoiceFactory> ListVoicesWindows()
    {
        var results = new List<IVoiceFactory>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var categoryId in VoiceCategoryIds)
        {
            object? categoryObj = null;
            try
            {
                categoryObj = CreateComObject("SAPI.SpObjectTokenCategory");
                if (categoryObj is null)
                {
                    continue;
                }

                dynamic category = categoryObj;
                category.SetId(categoryId, false);
                dynamic tokens = category.EnumerateTokens();
                var count = (int)tokens.Count;

                for (var i = 0; i < count; i++)
                {
                    dynamic token = tokens.Item(i);
                    string name = SafeToString(token.GetDescription());
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = token.GetAttribute("Name");
                    }
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    {
                        var locale = ParseSapiLanguageHex(SafeToString(token.GetAttribute("Language")));
                        var info = new VoiceInfo
                        {
                            Id = $"sapi:{name}",
                            DisplayName = name,
                            LocaleOrLanguage = locale
                        };
                        results.Add(new SapiVoiceFactory(info, token.Id));
                    }
                    ReleaseCom(token);
                }

                ReleaseCom(tokens);
            }
            catch
            {
            }
            finally
            {
                ReleaseCom(categoryObj);
            }
        }

        return results;
    }

    [SupportedOSPlatform("windows")]
    private static object? CreateComObject(string progId)
    {
        var comType = Type.GetTypeFromProgID(progId, throwOnError: false);
        return comType is null ? null : Activator.CreateInstance(comType);
    }

    private static string SafeToString(object? value)
    {
        return value?.ToString() ?? string.Empty;
    }

    private static string? ParseSapiLanguageHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var first = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        if (!ushort.TryParse(first, System.Globalization.NumberStyles.HexNumber, null, out var lcid))
        {
            return null;
        }

        try
        {
            return new System.Globalization.CultureInfo(lcid).Name;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseCom(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
