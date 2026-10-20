// Shared physical-line word-list rules, 2026-10-20. AGPL-3.0-or-later.
namespace DictationAssistant.App.Helpers;

internal static class WordListText
{
    // Count CRLF, LF and CR lines without allocating a string for every entry.
    // Unicode whitespace and duplicate entries follow the editor's existing rules.
    public static int CountEntries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var count = 0;
        var hasContent = false;
        foreach (var character in text)
        {
            if (character is '\r' or '\n')
            {
                if (hasContent) count++;
                hasContent = false;
            }
            else if (!char.IsWhiteSpace(character))
            {
                hasContent = true;
            }
        }
        return hasContent ? count + 1 : count;
    }
}
