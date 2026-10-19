// Scoring and output formatting are kept independent of Avalonia for testing.
using System.Text;

namespace DictationAssistant.Practices.Typing;

public static class TypingEvaluation
{
    public static bool IsCorrect(string expected, string entered, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(expected.Trim().Normalize(NormalizationForm.FormC),
            entered.Trim().Normalize(NormalizationForm.FormC), comparison);
    }

    public static string Mask(string word)
    {
        var tokens = word.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parts = tokens.Select(token => new string('_', Math.Clamp(token.Length, 1, 24)));
        var mask = string.Join("   ", parts);
        return mask.Length <= 100 ? mask : new string('_', 24);
    }

    public static double Accuracy(int correct, int total) => total <= 0 ? 0 : 100d * correct / total;

    public static string CsvCell(string text)
    {
        // Avoid accidental spreadsheet formula execution from imported/typed text.
        var safe = text.Replace('\r', ' ').Replace('\n', ' ');
        if (safe.Length > 0 && "=+-@\t".Contains(safe[0])) safe = "'" + safe;
        return '"' + safe.Replace("\"", "\"\"") + '"';
    }
}
