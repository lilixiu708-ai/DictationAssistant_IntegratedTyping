using System.Diagnostics;

namespace DictationAssistant.App.Voice;

internal static class ProcessRunner
{
    public static async Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? stdin,
        CancellationToken cancellationToken)
    {
        var (_, exitCode, error, _) = await RunInternalAsync(fileName, arguments, stdin, cancellationToken).ConfigureAwait(false);
        if (exitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException($"TTS process '{fileName}' failed with code {exitCode}: {error}");
    }

    public static async Task<string> RunCaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? stdin,
        CancellationToken cancellationToken)
    {
        var (output, exitCode, error, _) = await RunInternalAsync(fileName, arguments, stdin, cancellationToken).ConfigureAwait(false);
        if (exitCode == 0)
        {
            return output;
        }

        throw new InvalidOperationException($"TTS process '{fileName}' failed with code {exitCode}: {error}");
    }

    private static async Task<(string output, int exitCode, string error, bool success)> RunInternalAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? stdin,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = stdin is not null,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
            process.StandardInput.Close();
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return (output, process.ExitCode, error, process.ExitCode == 0);
    }
}
