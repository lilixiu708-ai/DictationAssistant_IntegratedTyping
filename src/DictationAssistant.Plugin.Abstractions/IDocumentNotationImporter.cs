// Optional document notation capability, 2026-10-20. AGPL-3.0-or-later.
namespace DictationAssistant.Plugin.Abstractions;

/// <summary>Preserves source superscript/subscript distinctions for spelling exercises.</summary>
/// <remarks>
/// Uses the same ownership, cancellation and materialization rules as IDocumentImporter.
/// DOCX readers express scripts using ^ and _ groups instead of flattening them into
/// surrounding text. Ordinary ReadAsync remains available for plain-text imports.
/// </remarks>
public interface IDocumentNotationImporter : IDocumentImporter
{
    Task<ImportDocument> ReadWithNotationAsync(
        Stream content, string fileName, CancellationToken cancellationToken);
}
