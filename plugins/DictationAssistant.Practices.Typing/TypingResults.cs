// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: keep scoring and CSV serialization independent of window interaction.
using System.Globalization;
using System.Text;

namespace DictationAssistant.Practices.Typing;

internal sealed record TypedResult(int Index, string Expected, string Entered, bool TimedOut, double ElapsedSeconds)
{
    public bool Correct { get; set; }
}

internal static class TypingResults
{
    public static int Score(IReadOnlyList<TypedResult> records, bool ignoreCase, CancellationToken cancellationToken)
    {
        var correct = 0;
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            record.Correct = TypingEvaluation.IsCorrect(record.Expected, record.Entered, ignoreCase);
            if (record.Correct) correct++;
        }
        return correct;
    }

    // The file picker owns the stream; leave its disposal to the caller.
    public static async Task WriteCsvAsync(Stream stream, IReadOnlyList<TypedResult> records)
    {
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
        await writer.WriteLineAsync("序号,正确词条,输入内容,是否正确,是否超时,耗时秒");
        foreach (var row in records)
        {
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"{row.Index},{TypingEvaluation.CsvCell(row.Expected)},{TypingEvaluation.CsvCell(row.Entered)},{(row.Correct ? "正确" : "错误")},{(row.TimedOut ? "是" : "否")},{row.ElapsedSeconds:F2}"));
        }
        await writer.FlushAsync();
    }
}
