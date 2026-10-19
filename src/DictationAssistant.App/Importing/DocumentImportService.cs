// Host-owned importer invocation and result validation, 2026-10-19. AGPL-3.0-or-later.
using DictationAssistant.App.Plugins;
using DictationAssistant.Plugin.Abstractions;
using System.Runtime.CompilerServices;

namespace DictationAssistant.App.Importing;

public static class DocumentImportService
{
    public const long MaximumInputBytes = 64L * 1024 * 1024;
    private const int MaximumRows = 100_000;
    private const int MaximumCharacters = 16_000_000;
    private static readonly ConditionalWeakTable<PluginEntry, SemaphoreSlim> ImportGates = new();

    /// <summary>
    /// Invokes the importer on a worker thread. The host owns the input stream
    /// and keeps it open until the actual importer task completes, even after
    /// the preview window requests cooperative cancellation.
    /// </summary>
    public static Task<ImportDocument> ReadAsync(PluginEntry plugin,
        Func<Task<Stream>> openReadAsync, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(openReadAsync);
        if (!plugin.IsLoaded || plugin.Importer is not { } importer)
            throw new InvalidOperationException("所选插件尚未成功加载。请查看插件管理中的错误信息。");

        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gate = ImportGates.GetValue(plugin, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Switching A → B → A may leave A's cancelled task finishing.
                // Do not enter the same third-party importer instance twice.
                await using var stream = await openReadAsync().ConfigureAwait(false);
                if (!stream.CanRead)
                    throw new InvalidDataException("无法读取这个文件。");
                if (stream.CanSeek && stream.Length > MaximumInputBytes)
                    throw new InvalidDataException("文件超过 64 MiB，请先拆分文档后重试。");

                var result = await importer.ReadAsync(stream, fileName, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return CopyAndValidate(result, cancellationToken);
            }
            finally { gate.Release(); }
        }, cancellationToken);
    }

    private static ImportDocument CopyAndValidate(ImportDocument? document, CancellationToken token)
    {
        if (document?.Sections is null || document.Sections.Count > 512 ||
            document.Warnings is null || document.Warnings.Count > 512)
            throw new InvalidDataException("插件返回的文档结构无效，或区域数量超过 512。");

        var sections = new List<ImportSection>(document.Sections.Count);
        var rowCount = 0;
        var characterCount = 0;
        string CopyText(string? value, int maximumLength = 200_000)
        {
            if (value is null || value.Length > maximumLength)
                throw new InvalidDataException("插件返回了空值或过长的文本字段。");
            characterCount = checked(characterCount + value.Length);
            if (characterCount > MaximumCharacters)
                throw new InvalidDataException("提取文本超过 1,600 万字符，请拆分文档。");
            return value;
        }

        foreach (var section in document.Sections)
        {
            token.ThrowIfCancellationRequested();
            if (section?.Columns is null || section.Columns.Count is < 1 or > 256 ||
                section.Rows is null || !Enum.IsDefined(section.Kind))
                throw new InvalidDataException("插件返回的表格列或正文区域无效。");
            rowCount = checked(rowCount + section.Rows.Count);
            if (rowCount > MaximumRows)
                throw new InvalidDataException("提取结果超过 100,000 行，请拆分文档。");

            var columns = section.Columns.Select(column => CopyText(column, 2_000)).ToArray();
            var rows = new List<ImportRow>(section.Rows.Count);
            foreach (var row in section.Rows)
            {
                token.ThrowIfCancellationRequested();
                if (row?.Cells is null || row.Cells.Count > columns.Length)
                    throw new InvalidDataException("插件返回的词条列数与区域定义不符。");
                rows.Add(new ImportRow
                {
                    SourceLabel = CopyText(row.SourceLabel, 2_000),
                    IsHeader = row.IsHeader,
                    Cells = Array.AsReadOnly(row.Cells.Select(cell => CopyText(cell)).ToArray())
                });
            }
            sections.Add(new ImportSection
            {
                Name = CopyText(section.Name, 2_000),
                Kind = section.Kind,
                Columns = Array.AsReadOnly(columns),
                SuggestedColumnIndex = Math.Clamp(section.SuggestedColumnIndex, 0, columns.Length - 1),
                Rows = rows.AsReadOnly()
            });
        }

        var warnings = document.Warnings.Select(warning => CopyText(warning, 10_000)).ToArray();
        return new ImportDocument
        {
            Sections = sections.AsReadOnly(),
            SuggestedSectionIndex = sections.Count == 0 ? 0 : Math.Clamp(document.SuggestedSectionIndex, 0, sections.Count - 1),
            Warnings = Array.AsReadOnly(warnings)
        };
    }
}
