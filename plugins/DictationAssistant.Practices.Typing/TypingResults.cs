// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-21: score confirmed Chinese alternatives from a frozen local snapshot.
using System.Globalization;
using System.Text;

namespace DictationAssistant.Practices.Typing;

internal sealed record TypedResult(int Index, string Expected, string Entered, bool TimedOut, double ElapsedSeconds)
{
    public bool Correct { get; set; }
    public string Prompt { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public string? AnswerPromptKey { get; init; }
}

internal static class TypingResults
{
    public static int Score(IReadOnlyList<TypedResult> records, bool ignoreCase, CancellationToken cancellationToken,
        bool chemicalAnswers = false,
        IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>>? acceptedAnswers = null,
        bool useAcceptedChineseAnswers = false)
    {
        var correct = 0;
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            record.Correct = chemicalAnswers
                ? ChemicalNotation.IsCorrect(record.Expected, record.Entered)
                : useAcceptedChineseAnswers && acceptedAnswers is not null
                ? PracticeAcceptedAnswers.IsAccepted(record.AnswerPromptKey ?? record.Prompt,
                    record.Expected, record.Entered, ignoreCase, acceptedAnswers)
                : TypingEvaluation.IsCorrect(record.Expected, record.Entered, ignoreCase);
            if (record.Correct) correct++;
        }
        return correct;
    }

    // The file picker owns the stream; leave its disposal to the caller.
    public static async Task WriteCsvAsync(Stream stream, IReadOnlyList<TypedResult> records)
    {
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
        await writer.WriteLineAsync("序号,正确词条,输入内容,是否正确,是否超时,耗时秒,提示,来源");
        foreach (var row in records)
        {
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"{row.Index},{TypingEvaluation.CsvCell(row.Expected)},{TypingEvaluation.CsvCell(row.Entered)},{(row.Correct ? "正确" : "错误")},{(row.TimedOut ? "是" : "否")},{row.ElapsedSeconds:F2},{TypingEvaluation.CsvCell(row.Prompt)},{TypingEvaluation.CsvCell(row.SourceLabel)}"));
        }
        await writer.FlushAsync();
    }
}
