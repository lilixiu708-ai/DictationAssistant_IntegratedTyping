// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-21: normalize only presentation differences in Chinese answers.
using System.Globalization;
using System.Text;

namespace DictationAssistant.Practices.Typing;

internal static class ChineseAnswerEvaluation
{
    public static bool ContainsChinese(string value) => value.EnumerateRunes().Any(rune => rune.Value is
        >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF
        or >= 0x20000 and <= 0x2FA1F or >= 0x30000 and <= 0x323AF);

    public static string NormalizeChinese(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var canonical = value.Normalize(NormalizationForm.FormC);
        var result = new StringBuilder(canonical.Length);
        var pendingSpace = false;
        foreach (var character in canonical)
        {
            // Width is presentation only. Do not use Form KC: it would also
            // change superscripts/subscripts and other meaningful symbols.
            var current = character is >= '\uFF01' and <= '\uFF5E'
                ? (char)(character - 0xFEE0) : character;
            if (current == '\t' || char.GetUnicodeCategory(current) == UnicodeCategory.SpaceSeparator)
            {
                pendingSpace = result.Length > 0;
                continue;
            }
            if (pendingSpace) result.Append(' ');
            result.Append(current);
            pendingSpace = false;
        }
        return result.ToString().Trim();
    }

    public static bool IsCorrect(string expected, string entered, bool ignoreCase)
    {
        if (!ContainsChinese(expected) || !ContainsChinese(entered))
            return TypingEvaluation.IsCorrect(expected, entered, ignoreCase);
        try
        {
            return string.Equals(NormalizeChinese(expected), NormalizeChinese(entered),
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // A malformed UTF-16 input is not an accepted answer.
            return false;
        }
    }
}
