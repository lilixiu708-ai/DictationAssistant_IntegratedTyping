// Word vocabulary document importer, 2026-10-19. AGPL-3.0-or-later.
using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.Importers.Docx;

/// <summary>Reads DOCX body tables and paragraphs without Office or external programs.</summary>
public sealed class DocxImporter : IDocumentImporter
{
    private const long MaxPackageBytes = 64L * 1024 * 1024;
    private const long MaxDocumentBytes = 32L * 1024 * 1024;
    private const int MaxArchiveEntries = 4096;
    private const int MaxTables = 256;
    private const int MaxRows = 50_000;
    private const int MaxColumns = 256;
    private const int MaxOutputCells = 500_000;
    private const int MaxXmlDepth = 128;

    private static readonly HashSet<string> VocabularyHeaders = new(StringComparer.Ordinal)
    {
        "单词", "词语", "词组", "词汇", "单词词组", "英文", "英语", "英文单词", "英语单词",
        "英文词汇", "英语词汇", "英文词组", "英语词组", "英文短语", "英语短语", "短语",
        "word", "words", "phrase", "phrases", "wordphrase", "wordsphrases", "vocabulary",
        "vocab", "english", "englishword", "englishwords", "englishphrase", "englishphrases",
        "englishvocabulary", "englishwordphrase", "term", "terms"
    };

    private static readonly HashSet<string> OtherHeaders = new(StringComparer.Ordinal)
    {
        "序号", "编号", "号", "词性", "中文", "汉语", "中文释义", "中文意思", "中文含义",
        "中文翻译", "释义", "词义", "意思", "解释", "定义", "音标", "发音", "例句", "搭配",
        "no", "number", "index", "id", "pos", "partofspeech", "wordclass", "wordtype",
        "chinese", "translation", "meaning", "meanings", "definition", "definitions",
        "chinesemeaning", "chinesetranslation", "pronunciation", "phonetic", "phonetics",
        "ipa", "example", "examples", "collocation", "collocations"
    };

    public async Task<ImportDocument> ReadAsync(
        Stream content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!content.CanRead)
            throw new ArgumentException("文件流不可读。", nameof(content));
        if (!string.Equals(Path.GetExtension(fileName), ".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("此插件支持 .docx 文件。旧版 .doc 请先另存为 .docx。");

        // Copy with an actual byte limit, including when the host provides a non-seekable stream.
        // Neither the input stream nor files referenced by the document are opened or modified here.
        using var package = await CopyBoundedAsync(
            content, MaxPackageBytes, "DOCX 文件超过 64 MiB。", cancellationToken).ConfigureAwait(false);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException("DOCX 包内的文件数量超过 4096，无法导入。");

        var documentEntries = archive.Entries
            .Where(entry => string.Equals(entry.FullName, "word/document.xml", StringComparison.Ordinal))
            .ToArray();
        if (documentEntries.Length != 1)
            throw new InvalidDataException("未找到唯一的 Word 正文。文件可能不是有效的 .docx 文档。");
        var documentEntry = documentEntries[0];
        if (documentEntry.Length > MaxDocumentBytes)
            throw new InvalidDataException("Word 正文 XML 超过 32 MiB，无法导入。");

        await using var entryStream = documentEntry.Open();
        using var documentBytes = await CopyBoundedAsync(
            entryStream, MaxDocumentBytes, "Word 正文 XML 超过 32 MiB，无法导入。", cancellationToken)
            .ConfigureAwait(false);
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDocumentBytes,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = false
        };
        // Check nesting before materializing XML or walking inline markup recursively.
        using (var validatingReader = XmlReader.Create(documentBytes, settings))
        {
            while (await validatingReader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (validatingReader.Depth > MaxXmlDepth)
                    throw new InvalidDataException("Word 正文 XML 嵌套层级超过 128，无法导入。");
            }
        }
        documentBytes.Position = 0;
        using var reader = XmlReader.Create(documentBytes, settings);
        var document = await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken)
            .ConfigureAwait(false);
        var root = document.Root;
        var ns = root?.Name.Namespace ?? XNamespace.None;
        if (root is null || root.Name.LocalName != "document" ||
            (ns.NamespaceName != "http://schemas.openxmlformats.org/wordprocessingml/2006/main" &&
             ns.NamespaceName != "http://purl.oclc.org/ooxml/wordprocessingml/main"))
            throw new InvalidDataException("Word 正文使用了不支持的 XML 格式。");
        var body = root.Element(ns + "body")
            ?? throw new InvalidDataException("Word 文档没有可读取的正文。");

        return ReadBody(body, ns, cancellationToken);
    }

    private static ImportDocument ReadBody(XElement body, XNamespace ns, CancellationToken cancellationToken)
    {
        var sections = new List<ImportSection>();
        var warnings = new List<string>();
        var tableFingerprints = new Dictionary<string, int>(StringComparer.Ordinal);
        var rowBudget = MaxRows;
        var cellBudget = MaxOutputCells;
        var suggestedSection = -1;
        var hasNestedTables = false;

        foreach (var table in body.Descendants(ns + "tbl"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsExcluded(table, ns))
                continue;
            if (table.Ancestors(ns + "tbl").Any())
            {
                hasNestedTables = true;
                continue;
            }
            if (sections.Count >= MaxTables)
                throw new InvalidDataException("正文中的表格超过 256 张，无法导入。");

            var tableNumber = sections.Count + 1;
            var section = ReadTable(table, ns, tableNumber, ref rowBudget, ref cellBudget, cancellationToken);
            sections.Add(section);
            if (suggestedSection < 0 && section.Columns.Any(IsVocabularyHeader))
                suggestedSection = sections.Count - 1;

            var fingerprint = Fingerprint(section, cancellationToken);
            if (tableFingerprints.TryGetValue(fingerprint, out var originalTable))
                warnings.Add($"表格 {tableNumber} 与表格 {originalTable} 的文字内容相同。默认仅选择一张表格；选择全部表格会保留重复项。");
            else
                tableFingerprints.Add(fingerprint, tableNumber);
        }

        var paragraphRows = new List<ImportRow>();
        var paragraphNumber = 0;
        foreach (var paragraph in body.Descendants(ns + "p"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (paragraph.Ancestors(ns + "tbl").Any() || IsExcluded(paragraph, ns))
                continue;
            paragraphNumber++;
            var text = ReadParagraphText(paragraph, ns);
            if (string.IsNullOrWhiteSpace(text))
                continue;
            ConsumeBudget(ref rowBudget, 1, "可提取的行数超过 50000。");
            ConsumeBudget(ref cellBudget, 1, "可提取的单元格数量超过 500000。");
            paragraphRows.Add(new ImportRow
            {
                SourceLabel = $"正文第 {paragraphNumber} 段",
                Cells = [text]
            });
        }
        if (paragraphRows.Count > 0)
            sections.Add(new ImportSection
            {
                Name = "正文段落",
                Kind = ImportSectionKind.Paragraphs,
                Columns = ["文字"],
                Rows = paragraphRows.ToArray()
            });

        if (!sections.Any(section => section.Rows.Any(row => row.Cells.Any(cell => !string.IsNullOrWhiteSpace(cell)))))
            throw new InvalidDataException("没有找到正文或表格文字。此插件不识别扫描图片，也不读取旧版 .doc 文件。");

        if (body.Descendants(ns + "r").Any(run =>
                !IsExcluded(run, ns) && run.Descendants(ns + "t").Any() &&
                string.Equals((string?)run.Element(ns + "rPr")?.Element(ns + "color")?.Attribute(ns + "val"),
                    "FFFFFF", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("检测到白色文字，插件已读取其中的文本。练习副本可能包含白色答案，请在预览中选择需要的表格。");
        if (body.Descendants(ns + "r").Any(run =>
                !IsExcluded(run, ns) && IsEnabled(run.Element(ns + "rPr")?.Element(ns + "vanish"), ns)))
            warnings.Add("检测到隐藏文字，插件已保留其文本，请在导入预览中核对。");
        if (hasNestedTables)
            warnings.Add("嵌套表格的文字合并在所属单元格中，没有重复生成独立表格。");
        if (body.Descendants().Any(element => element.Name == ns + "drawing" || element.Name == ns + "pict" || element.Name == ns + "object"))
            warnings.Add("文档含图片或嵌入对象；插件只读取正文及表格文字，不识别图片，也不打开嵌入文件。");

        cancellationToken.ThrowIfCancellationRequested();
        return new ImportDocument
        {
            Sections = sections.ToArray(),
            SuggestedSectionIndex = Math.Max(0, suggestedSection),
            Warnings = warnings.ToArray()
        };
    }

    private static ImportSection ReadTable(
        XElement table, XNamespace ns, int tableNumber,
        ref int rowBudget, ref int cellBudget, CancellationToken cancellationToken)
    {
        var data = new List<(string[] Cells, bool ExplicitHeader)>();
        var columnCount = table.Element(ns + "tblGrid")?.Elements(ns + "gridCol").Count() ?? 0;
        if (columnCount > MaxColumns)
            throw new InvalidDataException("表格列数超过 256，无法导入。");

        foreach (var row in table.Descendants(ns + "tr"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Ancestors(ns + "tbl").FirstOrDefault() != table || IsExcluded(row, ns))
                continue;
            ConsumeBudget(ref rowBudget, 1, "可提取的行数超过 50000。");
            var cells = new List<string>();
            var before = ReadGridSize(row.Element(ns + "trPr")?.Element(ns + "gridBefore"), ns, 0);
            for (var i = 0; i < before; i++)
                cells.Add(string.Empty);
            foreach (var cell in row.Descendants(ns + "tc"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cell.Ancestors(ns + "tr").FirstOrDefault() != row || IsExcluded(cell, ns))
                    continue;
                var span = ReadGridSize(cell.Element(ns + "tcPr")?.Element(ns + "gridSpan"), ns, 1);
                if (span < 1 || cells.Count + span > MaxColumns)
                    throw new InvalidDataException("表格列数或合并单元格范围超过 256，无法导入。");
                var paragraphs = cell.Descendants(ns + "p")
                    .Where(paragraph => !IsExcluded(paragraph, ns))
                    .Select(paragraph => ReadParagraphText(paragraph, ns));
                cells.Add(string.Join('\n', paragraphs));
                for (var i = 1; i < span; i++)
                    cells.Add(string.Empty);
            }
            var after = ReadGridSize(row.Element(ns + "trPr")?.Element(ns + "gridAfter"), ns, 0);
            if (cells.Count + after > MaxColumns)
                throw new InvalidDataException("表格列数超过 256，无法导入。");
            for (var i = 0; i < after; i++)
                cells.Add(string.Empty);
            columnCount = Math.Max(columnCount, cells.Count);
            data.Add((cells.ToArray(), IsEnabled(row.Element(ns + "trPr")?.Element(ns + "tblHeader"), ns)));
        }
        columnCount = Math.Max(1, columnCount);
        ConsumeBudget(ref cellBudget, checked(data.Count * columnCount), "可提取的单元格数量超过 500000。");

        // Headers belong near the start; avoid interpreting a later vocabulary item as a title.
        var headerIndex = data.FindIndex(0, Math.Min(data.Count, 10),
            row => row.ExplicitHeader || LooksLikeHeader(row.Cells));
        var header = headerIndex >= 0 ? data[headerIndex].Cells : [];
        var columns = Enumerable.Range(0, columnCount)
            .Select(index => index < header.Length && !string.IsNullOrWhiteSpace(header[index])
                ? header[index].Trim()
                : $"第 {index + 1} 列")
            .ToArray();
        var suggestedColumn = Array.FindIndex(columns, IsVocabularyHeader);
        var rows = new ImportRow[data.Count];
        for (var index = 0; index < data.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = data[index];
            var padded = new string[columnCount];
            Array.Fill(padded, string.Empty);
            Array.Copy(raw.Cells, padded, raw.Cells.Length);
            rows[index] = new ImportRow
            {
                SourceLabel = $"表格 {tableNumber}，第 {index + 1} 行",
                Cells = padded,
                IsHeader = raw.ExplicitHeader || index == headerIndex ||
                           (headerIndex >= 0 && SameHeader(raw.Cells, header))
            };
        }
        return new ImportSection
        {
            Name = $"表格 {tableNumber}",
            Kind = ImportSectionKind.Table,
            Columns = columns,
            SuggestedColumnIndex = Math.Max(0, suggestedColumn),
            Rows = rows
        };
    }

    private static string ReadParagraphText(XElement paragraph, XNamespace ns)
    {
        var text = new StringBuilder();
        AppendInline(paragraph, text, ns);
        return text.ToString();
    }

    private static void AppendInline(XElement element, StringBuilder text, XNamespace ns)
    {
        if (element.Name.Namespace != ns || IsExcludedElement(element, ns))
            return;
        if (element.Name == ns + "t")
            text.Append(element.Value);
        else if (element.Name == ns + "tab")
            text.Append('\t');
        else if (element.Name == ns + "br" || element.Name == ns + "cr")
            text.Append('\n');
        else if (element.Name == ns + "noBreakHyphen")
            text.Append('\u2011');
        else if (element.Name == ns + "softHyphen")
            text.Append('\u00ad');
        else if (element.Name != ns + "instrText" && element.Name != ns + "delText")
            foreach (var child in element.Elements())
                AppendInline(child, text, ns);
    }

    private static bool IsExcluded(XElement element, XNamespace ns) =>
        element.AncestorsAndSelf().Any(ancestor => IsExcludedElement(ancestor, ns));

    private static bool IsExcludedElement(XElement element, XNamespace ns) =>
        element.Name == ns + "del" || element.Name == ns + "moveFrom" ||
        element.Name == ns + "drawing" || element.Name == ns + "pict" ||
        element.Name == ns + "object" || element.Name == ns + "txbxContent";

    private static bool IsEnabled(XElement? element, XNamespace ns)
    {
        if (element is null)
            return false;
        var value = ((string?)element.Attribute(ns + "val"))?.ToLowerInvariant();
        return value is not ("0" or "false" or "off");
    }

    private static int ReadGridSize(XElement? element, XNamespace ns, int defaultValue)
    {
        if (element is null)
            return defaultValue;
        if (!int.TryParse((string?)element.Attribute(ns + "val"), out var size) || size < 0 || size > MaxColumns)
            throw new InvalidDataException("表格的列布局数据无效。");
        return size;
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> cells)
    {
        var nonEmpty = cells.Where(cell => !string.IsNullOrWhiteSpace(cell)).ToArray();
        if (nonEmpty.Length == 0)
            return false;
        var recognized = nonEmpty.Count(cell => IsVocabularyHeader(cell) || OtherHeaders.Contains(NormalizeHeader(cell)));
        return recognized >= Math.Min(2, nonEmpty.Length);
    }

    private static bool SameHeader(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count && left.Select(NormalizeHeader).SequenceEqual(right.Select(NormalizeHeader), StringComparer.Ordinal);

    private static bool IsVocabularyHeader(string text) => VocabularyHeaders.Contains(NormalizeHeader(text));

    private static string NormalizeHeader(string text) =>
        new(text.Where(character => char.IsLetterOrDigit(character)).Select(char.ToLowerInvariant).ToArray());

    private static string Fingerprint(ImportSection section, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in section.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // XML 1.0 cannot contain these separators, so row/cell boundaries stay unambiguous.
            foreach (var cell in row.Cells)
                hash.AppendData(Encoding.UTF8.GetBytes(cell.Trim() + '\u001f'));
            hash.AppendData("\u001e"u8);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void ConsumeBudget(ref int remaining, int used, string message)
    {
        if (used > remaining)
            throw new InvalidDataException(message);
        remaining -= used;
    }

    private static async Task<MemoryStream> CopyBoundedAsync(
        Stream input, long maximum, string error, CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    break;
                if (output.Length + count > maximum)
                    throw new InvalidDataException(error);
                output.Write(buffer, 0, count);
            }
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
