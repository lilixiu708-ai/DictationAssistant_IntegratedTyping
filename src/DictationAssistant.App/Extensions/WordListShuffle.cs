// Word-order shuffle extension, 2026-10-19. Licensed under AGPL-3.0-or-later.
using System.Text;
using DictationAssistant.App.Helpers;

namespace DictationAssistant.App.Extensions;

/// <summary>
/// Shuffles complete, nonblank lines while preserving their contents and the
/// original positions of blank lines and CRLF, LF, or CR line delimiters.
/// </summary>
public static class WordListShuffle
{
    private const int MaximumShuffleAttempts = 32;

    /// <summary>Counts nonblank physical lines, including duplicate entries.</summary>
    public static int CountEntries(string text) => WordListText.CountEntries(text);

    /// <summary>
    /// Returns whether there are at least two nonblank lines with different
    /// contents. Comparison is ordinal; entries are never trimmed or normalised.
    /// </summary>
    public static bool CanShuffle(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ReadOnlySpan<char> firstEntry = default;
        foreach (var line in ReadLines(text))
        {
            var entry = text.AsSpan(line.Offset, line.Length);
            if (entry.IsWhiteSpace())
                continue;

            if (firstEntry.IsEmpty)
                firstEntry = entry;
            else if (!entry.SequenceEqual(firstEntry))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns a different order whenever <see cref="CanShuffle"/> is true;
    /// otherwise returns the original string. Duplicate entries are retained.
    /// A trailing line delimiter is preserved exactly.
    /// </summary>
    public static string Shuffle(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = ReadLines(text).ToArray();
        var entries = new List<string>();
        foreach (var line in lines)
        {
            if (!text.AsSpan(line.Offset, line.Length).IsWhiteSpace())
                entries.Add(text.Substring(line.Offset, line.Length));
        }

        if (entries.Count < 2)
            return text;

        int firstDifferentIndex = 1;
        while (firstDifferentIndex < entries.Count
            && string.Equals(entries[0], entries[firstDifferentIndex], StringComparison.Ordinal))
        {
            firstDifferentIndex++;
        }
        if (firstDifferentIndex == entries.Count)
            return text;

        var shuffled = entries.ToArray();
        bool changed = false;
        for (int attempt = 0; attempt < MaximumShuffleAttempts && !changed; attempt++)
        {
            // Fisher–Yates: every index permutation is equally likely.
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            // Compare contents, not indices: exchanging identical entries is
            // still the same order from the user's perspective.
            for (int i = 0; i < shuffled.Length; i++)
            {
                if (!string.Equals(entries[i], shuffled[i], StringComparison.Ordinal))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            // Bound execution time and guarantee a visible change. Reaching
            // this fallback has probability at most 2^-32 for a valid input.
            // It introduces a correspondingly tiny bias towards this swap.
            (shuffled[0], shuffled[firstDifferentIndex]) =
                (shuffled[firstDifferentIndex], shuffled[0]);
        }

        var result = new StringBuilder(text.Length);
        int entryIndex = 0;
        foreach (var line in lines)
        {
            var originalContent = text.AsSpan(line.Offset, line.Length);
            if (originalContent.IsWhiteSpace())
                result.Append(originalContent);
            else
                result.Append(shuffled[entryIndex++]);

            // Delimiters belong to their physical positions, not to entries.
            // This also preserves mixed line endings and the trailing newline.
            result.Append(text, line.Offset + line.Length, line.DelimiterLength);
        }
        return result.ToString();
    }

    private static IEnumerable<PhysicalLine> ReadLines(string text)
    {
        int offset = 0;
        while (offset < text.Length)
        {
            int end = offset;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n')
                end++;

            int nextOffset = end;
            if (nextOffset < text.Length)
            {
                nextOffset++;
                if (text[end] == '\r' && nextOffset < text.Length && text[nextOffset] == '\n')
                    nextOffset++;
            }

            yield return new PhysicalLine(offset, end - offset, nextOffset - end);
            offset = nextOffset;
        }
    }

    private readonly record struct PhysicalLine(int Offset, int Length, int DelimiterLength);
}
