using System.Diagnostics;
using System.Text;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;

namespace DictationAssistant.App.Voice;

public sealed class EspeakNgVoice : IVoice
{
    private readonly string _voiceName;

    public EspeakNgVoice(string voiceName)
    {
        _voiceName = voiceName;
    }

    public async Task<PcmAudio> SynthesizePcmAsync(string text, VoiceSynthesisOptions options, CancellationToken ct)
    {
        var wavBytes = await SynthesizeWavAsync(text, options, ct).ConfigureAwait(false);
        if (wavBytes is null)
        {
            return PcmAudio.Empty;
        }
        return BassDecoder.FromStream(new MemoryStream(wavBytes));
    }

    private async Task<byte[]?> SynthesizeWavAsync(string text, VoiceSynthesisOptions options, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "espeak-ng",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = System.Text.Encoding.UTF8,
        };
        if (!string.IsNullOrWhiteSpace(_voiceName))
        {
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add(_voiceName);
        }
        if (options.Rate is int rate)
        {
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add((175 + Math.Clamp(rate, -10, 10) * 15).ToString());
        }
        startInfo.ArgumentList.Add("-b"); // Input encoding
        startInfo.ArgumentList.Add("1"); // UTF-8 input
        startInfo.ArgumentList.Add("--stdin"); // Input text from stdin
        startInfo.ArgumentList.Add("--stdout"); // Output WAV data to stdout
        using var process = Process.Start(startInfo);
        if (process == null) return null;
        process.Start();

        // Start reading output and error streams asynchronously
        var outputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, cancellationToken);
        var errorTask = ReadAllBytesAsync(process.StandardError.BaseStream, cancellationToken);

        // Write the input text and close stdin to signal end of input
        await process.StandardInput.WriteLineAsync(text).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();

        // Wait for both output and error reading to complete
        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        process.StandardOutput.BaseStream.Close();
        process.StandardError.BaseStream.Close();

        if (cancellationToken.IsCancellationRequested)
        {
            process.Kill();
            cancellationToken.ThrowIfCancellationRequested();
        }
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var utf8 = new UTF8Encoding
            {
                DecoderFallback = DecoderFallback.ReplacementFallback
            };
            var errorOutput = utf8.GetString(errorTask.Result);
            throw new Exception($"eSpeak NG exited with code {process.ExitCode}. Error output: {errorOutput}");
        }

        return outputTask.Result;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}

public sealed class EspeakNgVoiceFactory : IVoiceFactory
{
    private readonly VoiceInfo _info;
    private readonly string _voiceName;

    public EspeakNgVoiceFactory(VoiceInfo info, string voiceName)
    {
        _info = info;
        _voiceName = voiceName;
    }

    public VoiceInfo Info => _info;

    public IVoice Create()
    {
        return new EspeakNgVoice(_voiceName);
    }

    public override string ToString() => _info.DisplayName;
}

public sealed class EspeakNgVoiceFactoryProvider : IVoiceFactoryProvider
{
    public async Task<IReadOnlyList<IVoiceFactory>> GetFactoriesAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }
        try
        {
            var output = await ProcessRunner.RunCaptureAsync("espeak-ng", ["--voices"], null, cancellationToken).ConfigureAwait(false);
            var voices = new List<IVoiceFactory>();
            using var reader = new StringReader(output);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("Pty") || trimmed.StartsWith("-") || !char.IsDigit(trimmed[0]))
                {
                    continue;
                }

                var columns = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length < 4)
                {
                    continue;
                }
                var voiceName = columns[3].Replace('_', ' ');
                var info = new VoiceInfo
                {
                    Id = $"espeak:{columns[3]}",
                    DisplayName = $"{voiceName} (eSpeak NG)",
                    LocaleOrLanguage = columns[1],
                };
                voices.Add(new EspeakNgVoiceFactory(info, voiceName));
            }

            return voices;
        }
        catch
        {
            return [];
        }
    }
}
