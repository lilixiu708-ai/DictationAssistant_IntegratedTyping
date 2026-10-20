namespace DictationAssistant.Plugin.Abstractions;

/// <summary>One extension can implement several capability interfaces.</summary>
public interface ICommandPlugin
{
    IReadOnlyList<PluginCommand> Commands { get; }
    Task ExecuteAsync(string commandId, IPluginContext context, CancellationToken cancellationToken);
}

public sealed record PluginCommand(string Id, string Name, string Description = "");

/// <summary>Optional configuration UI, opened on the UI thread with the plugin manager as owner.</summary>
public interface IConfigurablePlugin
{
    /// <summary>
    /// The owner is an Avalonia window. Complete when the configuration window closes,
    /// and close that window when cancellation is requested.
    /// </summary>
    Task ConfigureAsync(object ownerWindow, CancellationToken cancellationToken);
}

/// <summary>Optional initialization and shutdown for any plugin kind.</summary>
public interface IPluginLifecycle
{
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
    Task ShutdownAsync(CancellationToken cancellationToken);
}

/// <summary>Receives coalesced snapshots after editing pauses; callbacks run off the UI thread.</summary>
public interface IWordListObserverPlugin
{
    Task OnWordListChangedAsync(PluginWordListSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed record PluginWordListSnapshot(string Text, string SelectedText, long Revision);

/// <summary>ExpectedRevision prevents overwriting user edits made after a snapshot.</summary>
public sealed record PluginWordListUpdate(string Text, bool Append = false, long? ExpectedRevision = null);

public enum PluginLogLevel
{
    Information,
    Warning,
    Error
}

/// <summary>Host services. Plugins remain trusted in-process .NET code, not a sandbox.</summary>
public interface IPluginContext
{
    string PluginId { get; }
    string HostVersion { get; }
    /// <summary>User-writable storage, separate from the installed plugin files.</summary>
    string DataDirectory { get; }
    /// <summary>Avalonia owner window, or null in the command-line host.</summary>
    object? OwnerWindow { get; }
    Task<PluginWordListSnapshot> GetWordListAsync(CancellationToken cancellationToken);
    /// <summary>The GUI host stops playback and applies one undoable edit.</summary>
    Task UpdateWordListAsync(PluginWordListUpdate update, CancellationToken cancellationToken);
    Task InvokeOnUiAsync(Func<Task> action, CancellationToken cancellationToken);
    Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken);
    Task SpeakAsync(string text, CancellationToken cancellationToken);
    /// <summary>Synthesize using the selected voice and the registered audio effects.</summary>
    Task<PluginAudioData> SynthesizeAsync(string text, int rate, CancellationToken cancellationToken);
    /// <summary>Play the supplied PCM as-is; ownership of its buffer is transferred to the host.</summary>
    Task PlayAudioAsync(PluginAudioData audio, CancellationToken cancellationToken);
    Task<PluginAudioData> ProcessAudioAsync(PluginAudioData audio, CancellationToken cancellationToken);
    /// <summary>Write a PCM WAV without closing destination. Audio is not processed again.</summary>
    Task ExportWaveAsync(PluginAudioData audio, Stream destination, CancellationToken cancellationToken);
    void Log(PluginLogLevel level, string message);
}
