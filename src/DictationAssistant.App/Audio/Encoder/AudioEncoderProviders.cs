using System.Diagnostics;

namespace DictationAssistant.App.Audio.Encoder;

public static class AudioEncoderProviders
{
    public static IReadOnlyList<IAudioEncoderFactory> GetAvailable()
    {
        var providers = new List<IAudioEncoderFactory>
        {
            new WaveEncoderFactory()
        };

        if (IsFFmpegAvailableAsync())
        {
            providers.Add(new FFmpegMp3EncoderFactory());
            providers.Add(new FFmpegOpusEncoderFactory());
        }

        return providers;
    }

    private static bool IsFFmpegAvailableAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            if (process == null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
