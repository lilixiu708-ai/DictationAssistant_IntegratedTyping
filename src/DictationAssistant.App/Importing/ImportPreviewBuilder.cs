// Common import preview rules, 2026-10-19. AGPL-3.0-or-later.
using System.Text.RegularExpressions;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Importing;

public sealed record ImportPreviewOptions(
    int SectionIndex = 0,
    int ColumnIndex = -1,
    bool SkipHeaders = true,
    bool Deduplicate = false,
    bool StripNumbering = true);

public sealed record ImportPreviewResult(
    string Text,
    int CandidateRows,
    int EntryCount,
    int SkippedHeaders,
    int SkippedEmpty,
    int RemovedDuplicates);

public sealed record WordListImportSelection(string Text, bool Append);

public static partial class ImportPreviewBuilder
{
    // Section -1 combines tables, or all paragraph sections if no tables exist.
    // Column -1 uses the plugin's suggested column separately for each section.
    public static ImportPreviewResult Build(ImportDocument document, ImportPreviewOptions options)
    {
        if (document.Sections.Count == 0)
        {
            return new ImportPreviewResult(string.Empty, 0, 0, 0, 0, 0);
        }

        IEnumerable<ImportSection> sections;
        if (options.SectionIndex == -1)
        {
            var tables = document.Sections.Where(s => s.Kind == ImportSectionKind.Table).ToArray();
            sections = tables.Length > 0 ? tables : document.Sections;
        }
        else
        {
            if (options.SectionIndex < 0 || options.SectionIndex >= document.Sections.Count)
                throw new ArgumentOutOfRangeException(nameof(options), "所选文档区域不存在。");
            sections = [document.Sections[options.SectionIndex]];
        }

        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int candidateRows = 0, skippedHeaders = 0, skippedEmpty = 0, removedDuplicates = 0;
        foreach (var section in sections)
        {
            var column = options.ColumnIndex == -1 ? section.SuggestedColumnIndex : options.ColumnIndex;
            foreach (var row in section.Rows)
            {
                candidateRows++;
                if (options.SkipHeaders && row.IsHeader)
                {
                    skippedHeaders++;
                    continue;
                }
                if (column < 0 || column >= row.Cells.Count)
                {
                    skippedEmpty++;
                    continue;
                }
                var value = NormalizeEntry(row.Cells[column], options.StripNumbering);
                if (value.Length == 0)
                {
                    skippedEmpty++;
                    continue;
                }
                if (options.Deduplicate && !seen.Add(value))
                {
                    removedDuplicates++;
                    continue;
                }
                entries.Add(value);
            }
        }
        return new ImportPreviewResult(string.Join(Environment.NewLine, entries), candidateRows,
            entries.Count, skippedHeaders, skippedEmpty, removedDuplicates);
    }

    public static string NormalizeEntry(string text, bool stripNumbering)
    {
        // One selected cell remains one entry; preserve phrases and letter case.
        var value = Whitespace().Replace(text, " ").Trim();
        return stripNumbering ? LeadingNumber().Replace(value, string.Empty).Trim() : value;
    }

    public static int CountEntries(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Count(line => !string.IsNullOrWhiteSpace(line));

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    // Require an explicit list delimiter; words such as "3D" and "21st" survive.
    [GeneratedRegex(@"^\s*(?:[（(]\d{1,6}[）)]\s*|\d{1,6}[、．)]\s*|\d{1,6}\.\s+)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumber();
}
