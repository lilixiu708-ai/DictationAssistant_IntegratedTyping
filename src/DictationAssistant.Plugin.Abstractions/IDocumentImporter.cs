// Document import plugin contract v1, 2026-10-19. AGPL-3.0-or-later.
namespace DictationAssistant.Plugin.Abstractions;

public static class PluginApi
{
    public const int Version = 1;
    /// <summary>Additive host capabilities; existing API 1 plugins still use the same contract assembly.</summary>
    public const int FeatureLevel = 2;
}

/// <summary>Reads a document without changing it or the application's word list.</summary>
/// <remarks>
/// The host owns and closes content. Leave that stream open. Return fully
/// materialized data, use the cancellation token, and do not access UI controls.
/// Plugins run in the host process; this API is not a security sandbox.
/// </remarks>
public interface IDocumentImporter
{
    Task<ImportDocument> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken);
}

public sealed class ImportDocument
{
    public IReadOnlyList<ImportSection> Sections { get; init; } = [];
    public int SuggestedSectionIndex { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public enum ImportSectionKind
{
    Table,
    Paragraphs
}

public sealed class ImportSection
{
    public string Name { get; init; } = string.Empty;
    public ImportSectionKind Kind { get; init; } = ImportSectionKind.Table;
    public IReadOnlyList<string> Columns { get; init; } = [];
    public int SuggestedColumnIndex { get; init; }
    public IReadOnlyList<ImportRow> Rows { get; init; } = [];
}

public sealed class ImportRow
{
    public string SourceLabel { get; init; } = string.Empty;
    public IReadOnlyList<string> Cells { get; init; } = [];
    public bool IsHeader { get; init; }
}
