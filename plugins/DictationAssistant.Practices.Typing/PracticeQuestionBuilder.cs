// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: pair prompt and answer cells from the same source row.
using System.Text;
using System.Text.RegularExpressions;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.Practices.Typing;

internal sealed record PracticeQuestionSet(IReadOnlyList<PracticeQuestion> Questions,
    int CandidateRows, int SkippedHeaders, int SkippedIncompleteRows);

internal static partial class PracticeQuestionBuilder
{
    public static PracticeQuestionSet Build(ImportDocument document, int sectionIndex,
        int promptColumn, int answerColumn, bool skipHeaders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (promptColumn < 0 || answerColumn < 0)
            throw new InvalidDataException("请选择提示列和答案列。");
        if (promptColumn == answerColumn)
            throw new InvalidDataException("提示列与答案列须不同，避免在提示中直接显示答案。");
        var sections = SelectSections(document, sectionIndex);
        if (!sections.Any(section => section.Columns.Count >= 2))
            throw new InvalidDataException("背诵配对需要至少两列的表格，请选择含名称与对应提示的 Word 或 Excel 文件。");
        var questions = new List<PracticeQuestion>();
        var candidates = 0;
        var headers = 0;
        var incomplete = 0;
        foreach (var section in sections)
        {
            foreach (var row in section.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates++;
                if (skipHeaders && row.IsHeader)
                {
                    headers++;
                    continue;
                }
                if (promptColumn >= row.Cells.Count || answerColumn >= row.Cells.Count)
                {
                    incomplete++;
                    continue;
                }
                var prompt = NormalizeCell(row.Cells[promptColumn]);
                var answer = NormalizeCell(row.Cells[answerColumn]);
                if (prompt.Length == 0 || answer.Length == 0)
                {
                    incomplete++;
                    continue;
                }
                questions.Add(new PracticeQuestion(prompt, answer, row.SourceLabel));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PracticeQuestionSet(questions.AsReadOnly(), candidates, headers, incomplete);
    }

    public static IReadOnlyList<ImportSection> SelectSections(ImportDocument document, int sectionIndex)
    {
        if (sectionIndex == -1)
            return document.Sections.Where(section => section.Kind == ImportSectionKind.Table).ToArray();
        if (sectionIndex < 0 || sectionIndex >= document.Sections.Count)
            throw new InvalidDataException("所选表格或工作表不存在。");
        var section = document.Sections[sectionIndex];
        if (section.Kind != ImportSectionKind.Table)
            throw new InvalidDataException("正文只有一列，请选择可配对的表格或工作表。");
        return [section];
    }

    public static (int PromptColumn, int AnswerColumn) GetDefaultColumns(ImportSection section)
    {
        var prompt = FindColumn(section, IsChemicalLabel);
        var answer = FindColumn(section, label => label is "englishname" or "englishterm" or "english"
            or "英文名称" or "英文名" or "英文" or "英文单词");
        if (answer < 0) answer = Math.Clamp(section.SuggestedColumnIndex, 0, Math.Max(0, section.Columns.Count - 1));
        if (prompt < 0 || prompt == answer)
            prompt = Enumerable.Range(0, section.Columns.Count).FirstOrDefault(index => index != answer, -1);
        return (prompt, answer);
    }

    public static bool IsChemicalColumn(ImportDocument document, int sectionIndex, int column)
    {
        if (column < 0) return false;
        var sections = SelectSections(document, sectionIndex);
        if (sections.Any(section => column < section.Columns.Count && IsChemicalLabel(NormalizeLabel(section.Columns[column]))))
            return true;
        var cells = sections.SelectMany(section => section.Rows)
            .Where(row => !row.IsHeader && column < row.Cells.Count)
            .Select(row => row.Cells[column]).Where(cell => !string.IsNullOrWhiteSpace(cell)).Take(64).ToArray();
        return cells.Length > 0 && cells.Count(HasChemicalNotation) * 2 >= cells.Length;
    }

    private static bool HasChemicalNotation(string value) => value.Contains('^') || value.Contains('_')
        || value.Any(character => "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻₀₁₂₃₄₅₆₇₈₉".Contains(character))
        || (value.TrimEnd().EndsWith('+') || value.TrimEnd().EndsWith('-')) && value.Any(char.IsUpper);

    private static int FindColumn(ImportSection section, Func<string, bool> predicate) =>
        Enumerable.Range(0, section.Columns.Count).FirstOrDefault(index => predicate(NormalizeLabel(section.Columns[index])), -1);

    private static bool IsChemicalLabel(string label) => label is "symbol" or "symbols" or "ion" or "ions"
        or "formula" or "chemicalformula" or "ionsymbol" or "离子式" or "离子符号" or "化学式" or "符号";

    private static string NormalizeLabel(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
            if (char.IsLetterOrDigit(character)) result.Append(char.ToLowerInvariant(character));
        return result.ToString();
    }

    private static string NormalizeCell(string value) => CellWhitespace().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex CellWhitespace();
}
