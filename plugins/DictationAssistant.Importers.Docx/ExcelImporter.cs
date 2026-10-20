// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: bounded local Excel worksheet extraction using ExcelDataReader 3.9.0.
using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using DictationAssistant.Plugin.Abstractions;
using ExcelDataReader;
using ExcelDataReader.Exceptions;

namespace DictationAssistant.Importers.Docx;

/// <summary>Returns one independent table per worksheet without evaluating formulas or running Office.</summary>
public sealed class ExcelImporter : IDocumentImporter
{
    private const long MaxInputBytes = 64L * 1024 * 1024;
    private const long MaxPartBytes = 32L * 1024 * 1024;
    private const long MaxInflatedBytes = 128L * 1024 * 1024;
    private const int MaxArchiveEntries = 4096;
    private const int MaxSheets = 256;
    private const int MaxRows = 50_000;
    private const int MaxColumns = 256;
    private const int MaxCells = 500_000;
    private const int MaxCharacters = 16_000_000;
    private const int MaxCellCharacters = 200_000;

    private static readonly HashSet<string> VocabularyHeaders = new(StringComparer.Ordinal)
    {
        "单词", "词语", "词组", "词汇", "英文", "英语", "英文名称", "英文单词", "英语单词", "英文词汇", "英语词汇", "英文短语", "英语短语", "短语",
        "word", "words", "phrase", "phrases", "vocabulary", "vocab", "english", "englishword", "englishwords",
        "englishphrase", "englishphrases", "englishvocabulary", "englishterm", "englishterms", "englishname", "term", "terms"
    };

    private static readonly HashSet<string> OtherHeaders = new(StringComparer.Ordinal)
    {
        "序号", "编号", "词性", "中文", "汉语", "中文名称", "中文释义", "中文意思", "中文翻译", "翻译", "释义", "意思", "解释", "定义", "音标", "发音", "例句", "备注", "说明",
        "no", "number", "index", "id", "pos", "partofspeech", "chinese", "translation", "meaning", "meanings", "definition", "definitions",
        "chinesemeaning", "chinesetranslation", "chinesename", "pronunciation", "phonetic", "phonetics", "ipa", "example", "examples", "note", "notes", "description"
    };

    public static bool Supports(string fileName) => Path.GetExtension(fileName).ToLowerInvariant()
        is ".xlsx" or ".xls" or ".xlsb" or ".xlsm";

    public async Task<ImportDocument> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();
        if (!content.CanRead) throw new ArgumentException("文件流不可读。", nameof(content));
        if (!Supports(fileName)) throw new InvalidDataException("Excel 导入支持 .xlsx、.xls、.xlsb 和 .xlsm 文件。");

        using var package = await CopyInputAsync(content, cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ReadWorkbook(package, Path.GetExtension(fileName), cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ExcelReaderException)
        {
            throw new InvalidDataException("无法读取 Excel 工作簿。请确认格式有效且已移除打开密码；可在 Excel 中另存为 .xlsx 后重试。");
        }
        catch (XmlException)
        {
            throw new InvalidDataException("Excel 包内的 XML 无效，或包含不支持的外部实体声明。请修复或重新另存文件。");
        }
        catch (Exception ex) when (ex is EndOfStreamException or FormatException or OverflowException or
                                      ArgumentException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("Excel 文件内容或单元格结构无效，请重新另存为 .xlsx 后重试。");
        }
    }

    private static ImportDocument ReadWorkbook(MemoryStream package, string extension, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var openXml = !extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);
        if (openXml) ValidateArchive(package, token);
        package.Position = 0;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var guarded = new GuardedReadStream(package, token);
        var configuration = new ExcelReaderConfiguration { LeaveOpen = true, SinglePassMode = false };
        using var reader = openXml
            ? ExcelReaderFactory.CreateOpenXmlReader(guarded, configuration)
            : ExcelReaderFactory.CreateBinaryReader(guarded, configuration);
        if (reader.ResultsCount is < 1 or > MaxSheets)
            throw new InvalidDataException("Excel 工作表数量必须为 1–256 张。");

        var sections = new List<ImportSection>();
        var warnings = new List<string>
        {
            "Excel 只读取已存储的单元格值和公式缓存结果，不计算公式、不执行宏或访问外部链接。缺少缓存的公式可能为空；需要时先在 Excel 中重算并保存。",
            "文本按原字符保留；数字使用固定小数点，日期采用不含时区的 ISO 日期时间，时长采用固定格式，不模拟 Excel 自定义显示格式。"
        };
        var rowBudget = MaxRows;
        var cellBudget = MaxCells;
        var characterBudget = MaxCharacters;
        var suggestedSection = -1;
        do
        {
            token.ThrowIfCancellationRequested();
            if (sections.Count >= MaxSheets)
                throw new InvalidDataException("Excel 工作表超过 256 张。");
            if (reader.FieldCount > MaxColumns || reader.RowCount > MaxRows)
                throw new InvalidDataException("单张 Excel 工作表超过 256 列或 50000 行，请缩小使用范围或拆分工作簿。");
            var name = reader.Name ?? $"工作表 {sections.Count + 1}";
            var merges = reader.MergeCells ?? [];
            var rows = new List<string[]>();
            var columnCount = Math.Max(1, reader.FieldCount);
            var hiddenRows = false;
            var errors = false;
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                Consume(ref rowBudget, 1, "Excel 总行数超过 50000 行。");
                if (reader.FieldCount > MaxColumns)
                    throw new InvalidDataException("Excel 工作表超过 256 列。");
                columnCount = Math.Max(columnCount, reader.FieldCount);
                var cells = new string[columnCount];
                Array.Fill(cells, string.Empty);
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    token.ThrowIfCancellationRequested();
                    var error = reader.GetCellError(column);
                    errors |= error is not null;
                    var value = error is { } cellError ? ErrorText(cellError) : CellText(reader.GetValue(column));
                    if (value.Length > MaxCellCharacters)
                        throw new InvalidDataException("Excel 单元格文字超过 200000 字符。");
                    Consume(ref characterBudget, value.Length, "Excel 提取文字超过 1600 万字符。");
                    cells[column] = value;
                }
                hiddenRows |= reader.RowHeight == 0;
                rows.Add(cells);
                if ((long)rows.Count * columnCount > cellBudget)
                    throw new InvalidDataException("Excel 补齐空位后的单元格总数超过 500000。");
            }
            Consume(ref cellBudget, checked(rows.Count * columnCount), "Excel 单元格总数超过 500000。");
            var section = CreateSection(name, rows, columnCount, merges, token);
            if (suggestedSection < 0 && section.Columns.Any(IsVocabularyHeader))
                suggestedSection = sections.Count;
            sections.Add(section);

            var details = new List<string>();
            if (!string.Equals(reader.VisibleState, "visible", StringComparison.OrdinalIgnoreCase))
                details.Add("隐藏工作表已保留");
            if (hiddenRows || Enumerable.Range(0, reader.FieldCount).Any(column => reader.GetColumnWidth(column) == 0))
                details.Add("隐藏行或列的文字已保留");
            if (merges.Length > 0)
                details.Add("合并区域只保留原始单元格内容，不向其他位置复制");
            if (errors) details.Add("错误单元格按 #REF! 等错误文本保留，请核对");
            if (details.Count > 0) warnings.Add($"工作表“{name}”：{string.Join("；", details)}。");
        } while (reader.NextResult());
        token.ThrowIfCancellationRequested();
        return new ImportDocument
        {
            Sections = sections.ToArray(), SuggestedSectionIndex = Math.Max(0, suggestedSection), Warnings = warnings.ToArray()
        };
    }

    private static ImportSection CreateSection(string name, List<string[]> data, int columnCount,
        CellRange[] merges, CancellationToken token)
    {
        var headerIndex = data.FindIndex(0, Math.Min(10, data.Count), LooksLikeHeader);
        var header = headerIndex >= 0 ? data[headerIndex] : [];
        var columns = Enumerable.Range(0, columnCount).Select(index =>
            index < header.Length && !string.IsNullOrWhiteSpace(header[index])
                ? header[index].Trim() : $"{ColumnName(index)} 列").ToArray();
        var suggested = Array.FindIndex(columns, IsVocabularyHeader);
        var rows = new ImportRow[data.Count];
        for (var index = 0; index < data.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var raw = data[index];
            var cells = new string[columnCount];
            Array.Fill(cells, string.Empty);
            Array.Copy(raw, cells, raw.Length);
            rows[index] = new ImportRow
            {
                SourceLabel = $"工作表“{name}”，第 {index + 1} 行",
                Cells = cells,
                IsHeader = LooksLikeHeader(raw) ||
                    (headerIndex >= 0 && columnCount > 1 && IsMergedTitle(raw, index, columnCount, merges))
            };
        }
        return new ImportSection
        {
            Name = name, Kind = ImportSectionKind.Table, Columns = columns,
            SuggestedColumnIndex = Math.Max(0, suggested), Rows = rows
        };
    }

    private static bool LooksLikeHeader(string[] cells)
    {
        var nonEmpty = cells.Where(cell => !string.IsNullOrWhiteSpace(cell)).ToArray();
        if (!nonEmpty.Any(IsVocabularyHeader)) return false;
        var recognized = nonEmpty.Count(cell => IsVocabularyHeader(cell) || IsOtherHeader(cell));
        return recognized >= Math.Min(2, nonEmpty.Length);
    }

    private static bool IsOtherHeader(string text)
    {
        var normalized = NormalizeHeader(text);
        return OtherHeaders.Contains(normalized) || normalized.StartsWith("notedescription", StringComparison.Ordinal);
    }

    private static bool IsVocabularyHeader(string text) => VocabularyHeaders.Contains(NormalizeHeader(text));
    private static string NormalizeHeader(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool IsMergedTitle(string[] cells, int rowIndex, int columnCount, CellRange[] merges)
    {
        var populated = cells.Where(cell => !string.IsNullOrWhiteSpace(cell)).Take(2).ToArray();
        if (populated.Length != 1) return false;
        return merges.Any(range => range.FromRow == rowIndex && range.ToRow == rowIndex &&
            range.FromColumn == 0 && range.ToColumn >= columnCount - 1);
    }

    private static string CellText(object? value) => value switch
    {
        null or DBNull => string.Empty,
        string text => text,
        DateTime date => date.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        bool flag => flag ? "TRUE" : "FALSE",
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => throw new InvalidDataException("Excel 返回了不支持的单元格值类型。")
    };

    private static string ErrorText(CellError error) => error switch
    {
        CellError.NULL => "#NULL!", CellError.DIV0 => "#DIV/0!", CellError.VALUE => "#VALUE!",
        CellError.REF => "#REF!", CellError.NAME => "#NAME?", CellError.NUM => "#NUM!",
        CellError.NA => "#N/A", CellError.GETTING_DATA => "#GETTING_DATA", _ => "#ERROR!"
    };

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var number = index + 1; number > 0; number = (number - 1) / 26)
            name = (char)('A' + (number - 1) % 26) + name;
        return name;
    }

    private static void Consume(ref int remaining, int used, string message)
    {
        if (used > remaining) throw new InvalidDataException(message);
        remaining -= used;
    }

    private static async Task<MemoryStream> CopyInputAsync(Stream input, CancellationToken token)
    {
        var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > MaxInputBytes)
                    throw new InvalidDataException("Excel 文件超过 64 MiB，请先拆分工作簿。");
                output.Write(buffer, 0, count);
            }
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static void ValidateArchive(MemoryStream package, CancellationToken token)
    {
        if (package.Length < 4 || package.GetBuffer()[0] != 'P' || package.GetBuffer()[1] != 'K')
            throw new InvalidDataException("此 Excel 文件不是可读取的工作簿包，或设有打开密码。请先在 Excel 中解密并另存。");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException("Excel 包内项目超过 4096 个。");
        var budget = new InflationBudget();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(entry.FullName)) throw new InvalidDataException("Excel 包包含重名项目，无法可靠读取。");
            if (entry.Length > MaxPartBytes) throw new InvalidDataException("Excel 包内单个项目超过 32 MiB。");
            using var original = entry.Open();
            using var stream = new GuardedReadStream(original, token, budget);
            if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                using var xml = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxPartBytes, MaxCharactersFromEntities = 1024,
                    IgnoreComments = true, IgnoreProcessingInstructions = true, CloseInput = false
                });
                while (xml.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (xml.Depth > 128) throw new InvalidDataException("Excel XML 嵌套深度超过 128 层。");
                    if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "sst" &&
                        xml.GetAttribute("uniqueCount") is { } count &&
                        (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var unique) || unique > MaxCells))
                        throw new InvalidDataException("Excel 共享文本项目数量超过 500000 或无效。");
                }
                // Account for trailing bytes even if an XML reader finishes before the ZIP stream does.
            }
            var buffer = new byte[8192];
            while (stream.Read(buffer) > 0) { }
        }
        // BIFF12 rents a buffer using each record's declared size. Check the
        // actual relationship-selected binary parts before the library can do so.
        foreach (var path in XlsbRecordValidator.FindWorkbookParts(archive, token))
        {
            var entry = archive.GetEntry(path)!;
            using var original = entry.Open();
            using var stream = new GuardedReadStream(original, token);
            XlsbRecordValidator.Validate(stream, entry.Length, token);
        }
    }

    private sealed class InflationBudget { public long Bytes; }

    /// <summary>Checks cancellation inside the library's synchronous probe, metadata and cell reads.</summary>
    private sealed class GuardedReadStream(Stream inner, CancellationToken token, InflationBudget? budget = null) : Stream
    {
        private long _partBytes;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { token.ThrowIfCancellationRequested(); inner.Position = value; } }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            var count = inner.Read(buffer);
            if (budget is not null)
            {
                _partBytes += count;
                budget.Bytes += count;
                if (_partBytes > MaxPartBytes || budget.Bytes > MaxInflatedBytes)
                    throw new InvalidDataException("Excel 实际解压超过单项 32 MiB 或总计 128 MiB 限制。");
            }
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) { token.ThrowIfCancellationRequested(); return inner.Seek(offset, origin); }
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        // The caller owns the wrapped stream, matching the plugin's leave-open contract.
        protected override void Dispose(bool disposing) { }
    }
}
