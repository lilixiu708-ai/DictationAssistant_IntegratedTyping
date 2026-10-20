// Word script notation for document-backed practice, 2026-10-20. AGPL-3.0-or-later.
using System.Text;
using System.Xml.Linq;

namespace DictationAssistant.Importers.Docx;

/// <summary>Preserves direct run superscripts/subscripts without changing ordinary imports.</summary>
internal static class DocxNotationReader
{
    public static string ReadParagraphText(XElement paragraph, XNamespace ns, CancellationToken token)
    {
        var writer = new NotationWriter();
        AppendInline(paragraph, writer, ns, Script.Baseline, token);
        return writer.Finish();
    }

    private static void AppendInline(
        XElement element, NotationWriter writer, XNamespace ns, Script script, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (element.Name.Namespace != ns || DocxImporter.IsExcludedElement(element, ns))
            return;
        if (element.Name == ns + "r")
        {
            var alignment = (string?)element.Element(ns + "rPr")?.Element(ns + "vertAlign")?.Attribute(ns + "val");
            script = alignment switch
            {
                "superscript" => Script.Superscript,
                "subscript" => Script.Subscript,
                _ => Script.Baseline
            };
        }
        if (element.Name == ns + "t")
            writer.Append(element.Value, script);
        else if (element.Name == ns + "tab")
            writer.Append("\t", Script.Baseline);
        else if (element.Name == ns + "br" || element.Name == ns + "cr")
            writer.Append("\n", Script.Baseline);
        else if (element.Name == ns + "noBreakHyphen")
            writer.Append("\u2011", script);
        else if (element.Name == ns + "softHyphen")
            writer.Append("\u00ad", script);
        else if (element.Name != ns + "instrText" && element.Name != ns + "delText")
            foreach (var child in element.Elements())
                AppendInline(child, writer, ns, script, token);
    }

    private enum Script { Baseline, Subscript, Superscript }

    private sealed class NotationWriter
    {
        private readonly StringBuilder _text = new();
        private readonly StringBuilder _group = new();
        private Script _script;

        public void Append(string value, Script script)
        {
            if (value.Length == 0) return;
            if (_script != script)
            {
                Flush();
                _script = script;
            }
            _group.Append(value);
        }

        public string Finish()
        {
            Flush();
            return _text.ToString();
        }

        private void Flush()
        {
            if (_group.Length == 0) return;
            var value = _group.ToString();
            if (_script == Script.Baseline)
                _text.Append(value);
            else
            {
                _text.Append(_script == Script.Subscript ? '_' : '^');
                // Numeric subscripts and charge groups are unambiguous in chemical formulas.
                // Other scripts retain explicit group boundaries instead of blending into baseline text.
                var compact = _script == Script.Subscript
                    ? value.All(char.IsAsciiDigit)
                    : IsCharge(value);
                if (compact) _text.Append(value);
                else _text.Append('{').Append(value).Append('}');
            }
            _group.Clear();
        }

        private static bool IsCharge(string value)
        {
            if (value[^1] is not ('+' or '-' or '\u2212')) return false;
            return value.AsSpan(0, value.Length - 1).IndexOfAnyExceptInRange('0', '9') < 0;
        }
    }
}
