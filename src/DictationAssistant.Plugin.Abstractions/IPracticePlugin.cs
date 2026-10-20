// Optional practice-plugin contracts; document import API v1 remains binary compatible.
namespace DictationAssistant.Plugin.Abstractions;

/// <summary>Practice plugins run with application permissions, not in a sandbox.</summary>
public interface IPracticePlugin
{
    /// <summary>Run a modal exercise. Return only after playback has stopped.</summary>
    Task RunAsync(IPracticeHost host, CancellationToken cancellationToken);
}

public interface IPracticeHost
{
    /// <summary>Avalonia Window instance for dialog ownership; no Avalonia dependency in this contract.</summary>
    object OwnerWindow { get; }
    /// <summary>Immutable, nonempty terms. Snapshot taken before the modal dialog opens.</summary>
    IReadOnlyList<string> Words { get; }
    /// <summary>Current auto-play setting, clamped to at least 1.</summary>
    int TimesPerWord { get; }
    string IntervalDescription { get; }
    /// <summary>Delay only between repeat plays of the same term.</summary>
    TimeSpan GetRepeatInterval(string word);
    /// <summary>Finishes only when speech synthesis AND device playback both end.</summary>
    Task SpeakOnceAsync(string word, CancellationToken cancellationToken);
}

/// <summary>Optional source count, independently calculated from the editor snapshot before parsing.</summary>
public interface IPracticeWordListMetadata
{
    /// <summary>Nonempty source lines, including duplicates. This is not the number of answered words.</summary>
    int ExpectedWordCount { get; }
}
