// Minimal document-import plugin example, 2026-10-19. AGPL-3.0-or-later.
using System.Text;
using DictationAssistant.Plugin.Abstractions;

namespace Example.PhrasesImporter;

public sealed class PhrasesImporter : IDocumentImporter
{
    public PhrasesImporter() { }

    public async Task<ImportDocument> ReadAsync(
        Stream content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();
        if (!content.CanRead)
            throw new InvalidDataException("文件流不可读。");
        if (!string.Equals(Path.GetExtension(fileName), ".phrases", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("本示例只读取 .phrases 文本文件。");
        if (content.CanSeek && content.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("文件超过 64 MiB。");

        using var reader = new StreamReader(content, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var rows = new List<ImportRow>();
        var lineNumber = 0;
        long readCharacters = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            readCharacters += line.Length + 1L;
            if (lineNumber > 50_000 || line.Length > 200_000 || readCharacters > 2_000_000)
                throw new InvalidDataException("本示例最多读取 50000 行及约 200 万字符，单行最多 20 万字符。");
            if (line.Contains('\0'))
                throw new InvalidDataException("文件包含二进制内容，请提供文本短语。");
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (rows.Count >= 10_000)
                throw new InvalidDataException("本示例最多导入 10000 条短语。");
            rows.Add(new ImportRow
            {
                SourceLabel = $"第 {lineNumber} 行",
                Cells = [line],
                IsHeader = false
            });
        }
        if (rows.Count == 0)
            throw new InvalidDataException("文件中没有非空短语。");

        return new ImportDocument
        {
            SuggestedSectionIndex = 0,
            Sections =
            [
                new ImportSection
                {
                    Name = "短语列表",
                    Kind = ImportSectionKind.Paragraphs,
                    Columns = ["短语"],
                    SuggestedColumnIndex = 0,
                    Rows = rows.ToArray()
                }
            ],
            Warnings = []
        };
    }
}
