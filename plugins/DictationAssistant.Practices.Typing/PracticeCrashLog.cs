// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DictationAssistant.Practices.Typing;

internal static class PracticeCrashLog
{
    private const int MaximumReports = 20;
    private const int MaximumExceptionCharacters = 32 * 1024;
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <param name="failedIndex">One-based index of the failed entry, if known.</param>
    public static Task<string> SaveAsync(string stage, int actualCount, int expectedCount,
        int? failedIndex, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(exception);
        // Directory creation and retention are synchronous filesystem operations;
        // keep the complete reporting path off the UI thread.
        return Task.Run(async () =>
        {
            await WriteGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolderOption.DoNotVerify);
                if (string.IsNullOrWhiteSpace(appData) || !Path.IsPathFullyQualified(appData))
                    throw new IOException("无法确定用于保存崩溃日志的用户数据目录。");

                var directory = Path.Combine(appData, "DictationAssistant", "Plugins",
                    "SynchronousTyping", "CrashLogs");
                Directory.CreateDirectory(directory);
                var timestamp = DateTimeOffset.UtcNow;
                var name = "crash-" + timestamp.ToString("yyyyMMdd'T'HHmmss.fffffff'Z'",
                    CultureInfo.InvariantCulture) + $"-{Guid.NewGuid():N}.log";
                var path = Path.Combine(directory, name);
                var details = exception.ToString();
                var report = new
                {
                    TimestampUtc = timestamp,
                    PluginVersion = typeof(PracticeCrashLog).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? typeof(PracticeCrashLog).Assembly.GetName().Version?.ToString(),
                    OperatingSystem = RuntimeInformation.OSDescription,
                    RuntimeVersion = RuntimeInformation.FrameworkDescription,
                    Stage = stage.Length <= 256 ? stage : stage[..256],
                    ActualCount = actualCount,
                    ExpectedCount = expectedCount,
                    FailedIndex = failedIndex,
                    Exception = details.Length <= MaximumExceptionCharacters
                        ? details : details[..MaximumExceptionCharacters],
                    ExceptionTruncated = details.Length > MaximumExceptionCharacters
                };

                var created = false;
                try
                {
                    await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 4096, FileOptions.Asynchronous);
                    created = true;
                    // Reporting a real failure must still work after the exercise is cancelled.
                    await JsonSerializer.SerializeAsync(stream, report, JsonOptions,
                        CancellationToken.None).ConfigureAwait(false);
                    await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    if (created)
                    {
                        try { File.Delete(path); }
                        catch (Exception ex) { Trace.WriteLine($"[TypingCrashLog] Cannot remove incomplete report {path}: {ex}"); }
                    }
                    throw;
                }

                PruneReports(directory, path);
                return path;
            }
            finally { WriteGate.Release(); }
        });
    }

    private static void PruneReports(string directory, string currentPath)
    {
        try
        {
            // UTC timestamps sort chronologically. Restrict retention to names
            // emitted by this class, and always retain the report just written.
            var oldReports = Directory.EnumerateFiles(directory, "crash-*.log")
                .Where(path => !string.Equals(path, currentPath, StringComparison.Ordinal) &&
                    Regex.IsMatch(Path.GetFileName(path),
                        @"^crash-\d{8}T\d{6}\.\d{7}Z-[0-9a-f]{32}\.log$", RegexOptions.CultureInvariant))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(MaximumReports - 1);
            foreach (var path in oldReports)
            {
                try { File.Delete(path); }
                catch (Exception ex) { Trace.WriteLine($"[TypingCrashLog] Cannot remove old report {path}: {ex}"); }
            }
        }
        catch (Exception ex) { Trace.WriteLine($"[TypingCrashLog] Cannot prune old reports: {ex}"); }
    }
}
