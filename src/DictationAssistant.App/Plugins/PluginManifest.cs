// Sideloaded document importer metadata, 2026-10-19. AGPL-3.0-or-later.
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Plugins;

/// <summary>Metadata read from a plugin folder before any plugin code runs.</summary>
public sealed class PluginManifest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Author { get; init; }
    public int ApiVersion { get; init; }
    public int MinimumHostFeatureLevel { get; init; } = 1;
    public string[] Extensions { get; init; } = [];
    public string EntryAssembly { get; init; } = string.Empty;
    public string EntryType { get; init; } = string.Empty;
    // Missing kind means a legacy document importer.
    public string Kind { get; init; } = "document-importer";
}

/// <summary>A loaded importer or a visible diagnostic for one plugin folder.</summary>
public sealed class PluginEntry
{
    internal PluginEntry(string folderPath)
    {
        FolderPath = folderPath;
    }

    public PluginManifest? Manifest { get; internal set; }
    public string FolderPath { get; }
    public object? Instance { get; internal set; }
    public IDocumentImporter? Importer => Instance as IDocumentImporter;
    public IPracticePlugin? PracticePlugin => Instance as IPracticePlugin;
    public ICommandPlugin? CommandPlugin => Instance as ICommandPlugin;
    public IConfigurablePlugin? ConfigurationPlugin => Instance as IConfigurablePlugin;
    public IVoiceProviderPlugin? VoiceProvider => Instance as IVoiceProviderPlugin;
    public IAudioEffectPlugin? AudioEffect => Instance as IAudioEffectPlugin;
    public IWordListObserverPlugin? WordListObserver => Instance as IWordListObserverPlugin;
    public IPluginLifecycle? Lifecycle => Instance as IPluginLifecycle;
    public IReadOnlyList<PluginCommand> Commands { get; internal set; } = [];
    public IReadOnlyList<string> Capabilities { get; internal set; } = [];
    public bool IsLoaded => Instance is not null && Error is null;
    public bool IsInitialized { get; internal set; }
    /// <summary>Saved preference. Changing it takes effect after restarting.</summary>
    public bool IsEnabled { get; internal set; } = true;
    public bool EnabledAtStartup { get; internal set; } = true;
    public string? Error { get; internal set; }
    public string? LastError { get; internal set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Manifest?.Name)
        ? Manifest.Name
        : Path.GetFileName(Path.TrimEndingDirectorySeparator(FolderPath));

    public string StatusText => Error is not null ? $"未加载：{Error}"
        : IsLoaded ? IsEnabled ? "已加载；更换或移除插件后请重启应用。" : "已加载；下次启动时禁用。"
        : !IsEnabled ? "已禁用；插件代码未执行。" : !EnabledAtStartup ? "将在下次启动时启用。" : "等待加载";

    // Keep the noncollectible context with its importer for the application's
    // lifetime. Replacing DLLs while they are loaded is deliberately unsupported.
    internal PluginLoadContext? LoadContext { get; set; }
    internal string? AssemblyPath { get; set; }
    internal bool CommandsFrozen { get; set; }
}
