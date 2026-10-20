// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: preserve stoichiometry and explicit charge when comparing ion spellings.
using System.Text;

namespace DictationAssistant.Practices.Typing;

internal static class ChemicalNotation
{
    private const int MaximumNotationLength = 4096;
    private const string SubscriptDigits = "₀₁₂₃₄₅₆₇₈₉";
    private const string SuperscriptDigits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
    private static readonly HashSet<string> Elements = new(
        ("H He Li Be B C N O F Ne Na Mg Al Si P S Cl Ar K Ca Sc Ti V Cr Mn Fe Co Ni Cu Zn " +
         "Ga Ge As Se Br Kr Rb Sr Y Zr Nb Mo Tc Ru Rh Pd Ag Cd In Sn Sb Te I Xe Cs Ba La Ce " +
         "Pr Nd Pm Sm Eu Gd Tb Dy Ho Er Tm Yb Lu Hf Ta W Re Os Ir Pt Au Hg Tl Pb Bi Po At Rn " +
         "Fr Ra Ac Th Pa U Np Pu Am Cm Bk Cf Es Fm Md No Lr Rf Db Sg Bh Hs Mt Ds Rg Cn Nh Fl Mc Lv Ts Og")
        .Split(' '), StringComparer.Ordinal);

    public static bool IsCorrect(string expected, string entered) =>
        string.Equals(Normalize(expected), Normalize(entered), StringComparison.Ordinal);

    /// <summary>
    /// Canonical form uses ordinary stoichiometric digits and a separate charge:
    /// SO_4^2- and SO₄²⁻ become SO4^2-. A baseline Fe2+ remains Fe2^+, never Fe^2+.
    /// Unrecognized text is preserved rather than guessed into a chemical formula.
    /// </summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim().Normalize(NormalizationForm.FormC);
        return TryNormalize(text, out var normalized) ? normalized : text;
    }

    /// <summary>Renders recognized formula indices and charge with Unicode scripts.</summary>
    public static string ToDisplay(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim().Normalize(NormalizationForm.FormC);
        if (!TryNormalize(text, out var normalized)) return value;
        var chargeIndex = normalized.IndexOf('^');
        var formulaLength = chargeIndex < 0 ? normalized.Length : chargeIndex;
        var output = new StringBuilder(normalized.Length);
        for (var index = 0; index < formulaLength; index++)
        {
            var character = normalized[index];
            output.Append(char.IsAsciiDigit(character) ? SubscriptDigits[character - '0'] : character);
        }
        if (chargeIndex >= 0)
        {
            for (var index = chargeIndex + 1; index < normalized.Length; index++)
            {
                var character = normalized[index];
                output.Append(char.IsAsciiDigit(character) ? SuperscriptDigits[character - '0']
                    : character == '+' ? '⁺' : '⁻');
            }
        }
        return output.ToString();
    }

    private static bool TryNormalize(string text, out string normalized)
    {
        normalized = string.Empty;
        if (text.Length is 0 or > MaximumNotationLength) return false;
        var chargeStart = text.IndexOf('^');
        string formula, charge;
        if (chargeStart >= 0)
        {
            formula = text[..chargeStart];
            charge = text[(chargeStart + 1)..];
            if (charge.StartsWith('{') && charge.EndsWith('}')) charge = charge[1..^1];
        }
        else
        {
            chargeStart = -1;
            for (var index = 0; index < text.Length; index++)
            {
                if (SuperscriptValue(text[index]) == '\0') continue;
                chargeStart = index;
                break;
            }
            if (chargeStart >= 0)
            {
                formula = text[..chargeStart];
                charge = text[chargeStart..];
            }
            else if (text[^1] is '+' or '-' or '−')
            {
                // Without an exponent marker, preceding digits belong to the formula.
                formula = text[..^1];
                charge = text[^1..];
            }
            else
            {
                formula = text;
                charge = string.Empty;
            }
        }

        if (!TryFormula(formula, out formula)) return false;
        if (chargeStart >= 0 || charge.Length > 0)
        {
            if (!TryCharge(charge, out charge)) return false;
            normalized = formula + '^' + charge;
        }
        else normalized = formula;
        return true;
    }

    private static bool TryCharge(string text, out string charge)
    {
        charge = string.Empty;
        if (text.Length == 0) return false;
        var output = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            var mapped = SuperscriptValue(character);
            output.Append(mapped != '\0' ? mapped : character == '−' ? '-' : character);
        }
        var value = output.ToString();
        if (value[^1] is not ('+' or '-')) return false;
        var magnitude = value.AsSpan(0, value.Length - 1);
        if (!magnitude.IsEmpty)
        {
            if (magnitude[0] == '0') return false;
            foreach (var character in magnitude)
                if (!char.IsAsciiDigit(character)) return false;
        }
        charge = magnitude.SequenceEqual("1") ? value[^1..] : value;
        return true;
    }

    private static bool TryFormula(string text, out string formula)
    {
        formula = string.Empty;
        var output = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '_')
            {
                if (++index >= text.Length) return false;
                var braced = text[index] == '{';
                if (braced && ++index >= text.Length) return false;
                var start = index;
                while (index < text.Length && (char.IsAsciiDigit(text[index]) || SubscriptValue(text[index]) != '\0'))
                {
                    output.Append(char.IsAsciiDigit(text[index]) ? text[index] : SubscriptValue(text[index]));
                    index++;
                }
                if (index == start) return false;
                if (braced)
                {
                    if (index >= text.Length || text[index] != '}') return false;
                }
                else index--;
            }
            else
            {
                var mapped = SubscriptValue(character);
                output.Append(mapped != '\0' ? mapped : character);
            }
        }

        var value = output.ToString();
        var groups = new Stack<char>();
        var hasItem = false;
        for (var index = 0; index < value.Length;)
        {
            var character = value[index];
            if (character is >= 'A' and <= 'Z')
            {
                var start = index++;
                if (index < value.Length && value[index] is >= 'a' and <= 'z') index++;
                if (!Elements.Contains(value[start..index])) return false;
                hasItem = true;
            }
            else if (character is '(' or '[')
            {
                if (groups.Count >= 32) return false;
                groups.Push(character == '(' ? ')' : ']');
                hasItem = false;
                index++;
                continue;
            }
            else if (character is ')' or ']')
            {
                if (!hasItem || groups.Count == 0 || groups.Pop() != character) return false;
                hasItem = true;
                index++;
            }
            else return false;

            if (index < value.Length && char.IsAsciiDigit(value[index]))
            {
                if (value[index] == '0') return false;
                do { index++; } while (index < value.Length && char.IsAsciiDigit(value[index]));
            }
        }
        if (!hasItem || groups.Count != 0) return false;
        formula = value;
        return true;
    }

    private static char SubscriptValue(char character)
    {
        var index = SubscriptDigits.IndexOf(character);
        return index < 0 ? '\0' : (char)('0' + index);
    }

    private static char SuperscriptValue(char character)
    {
        if (character == '⁺') return '+';
        if (character == '⁻') return '-';
        var index = SuperscriptDigits.IndexOf(character);
        return index < 0 ? '\0' : (char)('0' + index);
    }
}
