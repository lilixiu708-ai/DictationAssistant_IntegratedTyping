// Optional document-backed practice preparation, 2026-10-20. AGPL-3.0-or-later.
namespace DictationAssistant.Plugin.Abstractions;

/// <summary>Host feature level 5: select and read a document without changing the editor.</summary>
public interface IPracticeDocumentHost
{
    /// <summary>
    /// Called on the UI thread with an Avalonia Window that owns the file picker.
    /// Returns null when the user cancels. Importer work runs off the UI thread,
    /// and Word scientific notation is retained when the importer supports it.
    /// </summary>
    Task<PracticeDocument?> OpenDocumentAsync(object ownerWindow, CancellationToken cancellationToken);
}

public sealed record PracticeDocument(string FileName, ImportDocument Document);

/// <summary>A practice plugin that can select its own source before the exercise starts.</summary>
public interface IPracticeSourceSelectionPlugin
{
    bool CanStartWithoutWords { get; }
}
