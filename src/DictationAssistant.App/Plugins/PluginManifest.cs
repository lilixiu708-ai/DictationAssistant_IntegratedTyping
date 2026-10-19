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
    public IDocumentImporter? Importer { get; internal set; }
    public IPracticePlugin? PracticePlugin { get; internal set; }
    public bool IsLoaded => (Importer is not null || PracticePlugin is not null) && Error is null;
    public string? Error { get; internal set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Manifest?.Name)
        ? Manifest.Name
        : Path.GetFileName(Path.TrimEndingDirectorySeparator(FolderPath));

    public string StatusText => IsLoaded
        ? "已加载；更换或移除插件后请重启应用。"
        : Error is not null ? $"未加载：{Error}" : "等待加载";

    // Keep the noncollectible context with its importer for the application's
    // lifetime. Replacing DLLs while they are loaded is deliberately unsupported.
    internal PluginLoadContext? LoadContext { get; set; }
    internal string? AssemblyPath { get; set; }
}
