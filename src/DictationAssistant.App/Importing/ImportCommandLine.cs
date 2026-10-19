// Batch document extraction using the same host/plugin path, 2026-10-19. AGPL-3.0-or-later.
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using DictationAssistant.App.Plugins;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Importing;

/// <summary>Optional console commands; ordinary file arguments still open the GUI.</summary>
public static class ImportCommandLine
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static bool Handles(string[] args) =>
        args.Length > 0 && (args[0].StartsWith("--", StringComparison.Ordinal) || args[0] is "-h" or "-?");

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Utf8WithoutBom;
        Console.InputEncoding = Utf8WithoutBom;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            try
            {
                cancellation.Cancel();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"取消通知发生错误：{ex.Message}");
            }
        };
        Console.CancelKeyPress += cancelHandler;

        string? outputPath = null;
        string? reportPath = null;
        var outputCreated = false;
        var outputSaved = false;
        var reportSaved = false;
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h" or "-?")
            {
                PrintHelp();
                return 0;
            }

            var catalog = new PluginCatalog(Path.Combine(AppContext.BaseDirectory, "Plugins"));
            if (args.Length == 1 && args[0] == "--list-plugins")
            {
                await Task.Run(catalog.Load, cancellation.Token).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    catalog.RootDirectory,
                    ApiVersion = PluginApi.Version,
                    RequiresRestartForChanges = true,
                    LoadedCount = catalog.Entries.Count(entry => entry.IsLoaded),
                    catalog.SupportedExtensions,
                    Plugins = catalog.Entries.Select(entry => new
                    {
                        entry.Manifest,
                        entry.FolderPath,
                        entry.IsLoaded,
                        entry.DisplayName,
                        entry.StatusText,
                        entry.Error
                    }).ToArray()
                }, ReportJsonOptions));
                return 0;
            }

            var arguments = ParseExtractionArguments(args);
            ValidatePaths(arguments);
            outputPath = arguments.OutputFile;
            reportPath = arguments.ReportFile;

            await Task.Run(catalog.Load, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            var plugin = SelectPlugin(catalog, arguments);
            var document = await DocumentImportService.ReadAsync(plugin,
                () => Task.FromResult<Stream>(new FileStream(arguments.InputFile,
                    FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan)),
                Path.GetFileName(arguments.InputFile), cancellation.Token).ConfigureAwait(false);
            var options = new ImportPreviewOptions(
                arguments.SectionIndex ?? document.SuggestedSectionIndex,
                arguments.ColumnIndex,
                SkipHeaders: !arguments.IncludeHeaders,
                Deduplicate: arguments.Deduplicate,
                StripNumbering: !arguments.KeepNumbering);
            var selectedIndices = ValidateSelection(document, options);
            var preview = ImportPreviewBuilder.Build(document, options);
            if (preview.EntryCount == 0)
            {
                throw new InvalidDataException("所选区域和列没有可导出的词条。请检查 --section、--column 或 --include-headers。");
            }

            // Serialize the complete report before either destination is touched.
            var report = arguments.ReportFile is null ? null
                : BuildReport(arguments, plugin, document, options, preview, selectedIndices);
            cancellation.Token.ThrowIfCancellationRequested();
            await WriteTextAsync(arguments.OutputFile, preview.Text, arguments.Overwrite,
                cancellation.Token, () => outputCreated = true).ConfigureAwait(false);
            outputSaved = true;
            if (arguments.ReportFile is not null && report is not null)
            {
                await WriteTextAsync(arguments.ReportFile, report, arguments.Overwrite,
                    cancellation.Token, () => { }).ConfigureAwait(false);
                reportSaved = true;
            }

            Console.WriteLine($"已提取 {preview.EntryCount} 条词条（插件：{plugin.Manifest!.Id}）。");
            Console.WriteLine($"词表：{arguments.OutputFile}");
            if (arguments.ReportFile is not null)
            {
                Console.WriteLine($"报告：{arguments.ReportFile}");
            }
            foreach (var warning in document.Warnings.Take(20))
            {
                Console.Error.WriteLine($"提示：{warning}");
            }
            if (document.Warnings.Count > 20)
            {
                Console.Error.WriteLine($"另有 {document.Warnings.Count - 20} 条提示，可使用 --report 查看完整记录。");
            }
            return 0;
        }
        catch (CommandLineArgumentException ex)
        {
            Console.Error.WriteLine($"参数错误：{ex.Message}");
            Console.Error.WriteLine("使用 --help 查看命令格式。");
            return 2;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("操作已取消。");
            ExplainOutputState(outputCreated, outputSaved, outputPath, reportPath, reportSaved);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"提取失败：{ex.Message}");
            ExplainOutputState(outputCreated, outputSaved, outputPath, reportPath, reportSaved);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static ExtractionArguments ParseExtractionArguments(string[] args)
    {
        var result = new ExtractionArguments();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (!seen.Add(option))
            {
                throw new CommandLineArgumentException($"重复的选项：{option}");
            }

            string ReadValue()
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i]))
                {
                    throw new CommandLineArgumentException($"{option} 缺少值。");
                }
                return args[i];
            }

            switch (option)
            {
                case "--extract": result.InputFile = ReadValue(); break;
                case "--output": result.OutputFile = ReadValue(); break;
                case "--report": result.ReportFile = ReadValue(); break;
                case "--plugin": result.PluginId = ReadValue(); break;
                case "--section":
                    var section = ReadValue();
                    result.SectionIndex = section.Equals("all", StringComparison.OrdinalIgnoreCase)
                        ? -1 : ParsePositiveIndex(section, option);
                    break;
                case "--column":
                    var column = ReadValue();
                    result.ColumnIndex = column.Equals("auto", StringComparison.OrdinalIgnoreCase)
                        ? -1 : ParsePositiveIndex(column, option);
                    break;
                case "--deduplicate": result.Deduplicate = true; break;
                case "--include-headers": result.IncludeHeaders = true; break;
                case "--keep-numbering": result.KeepNumbering = true; break;
                case "--overwrite": result.Overwrite = true; break;
                default: throw new CommandLineArgumentException($"未知或位置不正确的选项：{option}");
            }
        }

        if (string.IsNullOrWhiteSpace(result.InputFile) || string.IsNullOrWhiteSpace(result.OutputFile))
        {
            throw new CommandLineArgumentException("提取需要同时指定 --extract INPUT 和 --output OUTPUT.txt。");
        }
        return result;
    }

    private static int ParsePositiveIndex(string value, string option)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 1)
        {
            throw new CommandLineArgumentException($"{option} 的编号从 1 开始，必须是正整数。");
        }
        return index - 1;
    }

    private static void ValidatePaths(ExtractionArguments arguments)
    {
        arguments.InputFile = NormalizePath(arguments.InputFile);
        arguments.OutputFile = NormalizePath(arguments.OutputFile);
        if (arguments.ReportFile is not null)
        {
            arguments.ReportFile = NormalizePath(arguments.ReportFile);
        }
        if (!File.Exists(arguments.InputFile))
        {
            throw new CommandLineArgumentException($"找不到输入文件：{arguments.InputFile}");
        }
        if (!Path.GetExtension(arguments.OutputFile).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommandLineArgumentException("--output 仅允许 .txt 文件，不能把提取内容写回 Word 文档。");
        }
        if (arguments.ReportFile is not null &&
            !Path.GetExtension(arguments.ReportFile).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommandLineArgumentException("--report 仅允许 .json 文件。");
        }

        var paths = new List<string> { arguments.InputFile, arguments.OutputFile };
        if (arguments.ReportFile is not null)
        {
            paths.Add(arguments.ReportFile);
        }
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count ||
            paths.Select(ResolveExistingLinks).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
        {
            throw new CommandLineArgumentException("输入文件、词表输出和报告必须是不同的文件路径。");
        }

        ValidateDestination(arguments.OutputFile, arguments.Overwrite);
        if (arguments.ReportFile is not null)
        {
            ValidateDestination(arguments.ReportFile, arguments.Overwrite);
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CommandLineArgumentException($"文件路径无效：{path}（{ex.Message}）");
        }
    }

    private static string ResolveExistingLinks(string path)
    {
        // Resolve existing directory junctions/symlinks as well as a leaf link,
        // so an alias cannot accidentally send the output back to the input.
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = target.FullName;
            }
        }
        return Path.GetFullPath(current);
    }

    private static void ValidateDestination(string path, bool overwrite)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Length == 0 || fileName.EndsWith('.') || fileName.EndsWith(' ') ||
            fileName.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
        {
            throw new CommandLineArgumentException($"输出文件名包含 Windows 不允许的字符：{fileName}");
        }
        if (Directory.Exists(path))
        {
            throw new CommandLineArgumentException($"输出路径是一个文件夹：{path}");
        }
        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            throw new CommandLineArgumentException($"输出文件夹不存在，请先创建：{Path.GetDirectoryName(path)}");
        }
        if (File.Exists(path) && !overwrite)
        {
            throw new CommandLineArgumentException($"文件已经存在：{path}。请改用新文件名，或明确加上 --overwrite。");
        }
    }

    private static PluginEntry SelectPlugin(PluginCatalog catalog, ExtractionArguments arguments)
    {
        var extension = Path.GetExtension(arguments.InputFile);
        if (arguments.PluginId is not null)
        {
            var entry = catalog.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Manifest?.Id, arguments.PluginId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                throw new CommandLineArgumentException($"没有找到插件 {arguments.PluginId}。可使用 --list-plugins 查看插件列表。");
            }
            if (!entry.IsLoaded)
            {
                throw new InvalidOperationException($"插件 {arguments.PluginId} 未能加载：{entry.Error}");
            }
            if (!entry.Manifest!.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new CommandLineArgumentException($"插件 {arguments.PluginId} 不支持 {extension} 文件。");
            }
            return entry;
        }

        var selected = catalog.GetImportersForFile(arguments.InputFile)
            .OrderBy(entry => entry.Manifest!.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (selected is not null)
        {
            return selected;
        }
        var failures = catalog.Entries.Where(entry => !entry.IsLoaded &&
                entry.Manifest?.Extensions?.Contains(extension, StringComparer.OrdinalIgnoreCase) == true)
            .Select(entry => $"{entry.DisplayName}：{entry.Error}");
        var details = string.Join("；", failures);
        throw new InvalidOperationException(
            $"没有可用的 {extension} 文档导入插件。请将完整插件文件夹放入 {catalog.RootDirectory} 后重新运行；可使用 --list-plugins 查看详情。" +
            (details.Length == 0 ? string.Empty : $" {details}"));
    }

    private static int[] ValidateSelection(ImportDocument document, ImportPreviewOptions options)
    {
        if (document.Sections.Count == 0)
        {
            throw new InvalidDataException("文档没有可提取的表格或正文区域。");
        }
        if (options.SectionIndex < -1 || options.SectionIndex >= document.Sections.Count)
        {
            throw new CommandLineArgumentException($"--section 超出范围，文档共有 {document.Sections.Count} 个区域。");
        }
        var indices = options.SectionIndex == -1
            ? Enumerable.Range(0, document.Sections.Count).Where(index =>
                document.Sections[index].Kind == ImportSectionKind.Table).ToArray()
            : [options.SectionIndex];
        if (indices.Length == 0)
        {
            indices = Enumerable.Range(0, document.Sections.Count).ToArray();
        }
        if (options.ColumnIndex >= 0 && indices.All(index =>
                options.ColumnIndex >= document.Sections[index].Columns.Count))
        {
            throw new CommandLineArgumentException($"所选区域都没有第 {options.ColumnIndex + 1} 列。");
        }
        // Match the GUI: when combining tables, a missing column in some
        // sections is counted as skipped instead of rejecting the whole import.
        return indices;
    }

    private static string BuildReport(ExtractionArguments arguments, PluginEntry plugin,
        ImportDocument document, ImportPreviewOptions options, ImportPreviewResult preview, int[] selectedIndices)
    {
        return JsonSerializer.Serialize(new
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            arguments.InputFile,
            arguments.OutputFile,
            PluginId = plugin.Manifest!.Id,
            PluginName = plugin.Manifest.Name,
            PluginVersion = plugin.Manifest.Version,
            PluginApiVersion = plugin.Manifest.ApiVersion,
            Options = new
            {
                Section = arguments.SectionIndex is null ? "suggested"
                    : arguments.SectionIndex == -1 ? "all" : (arguments.SectionIndex.Value + 1).ToString(CultureInfo.InvariantCulture),
                Column = options.ColumnIndex == -1 ? "auto" : (options.ColumnIndex + 1).ToString(CultureInfo.InvariantCulture),
                options.SkipHeaders,
                options.Deduplicate,
                options.StripNumbering,
                arguments.Overwrite,
                IndexBase = 1,
                AllSectionsMeaning = "有表格时选择所有表格；没有表格时选择所有正文区域"
            },
            Counts = new
            {
                TotalSections = document.Sections.Count,
                TotalRows = document.Sections.Sum(section => section.Rows.Count),
                preview.CandidateRows,
                preview.EntryCount,
                preview.SkippedHeaders,
                preview.SkippedEmpty,
                preview.RemovedDuplicates
            },
            document.Warnings,
            SuggestedSection = document.SuggestedSectionIndex + 1,
            Sections = document.Sections.Select((section, index) => new
            {
                Index = index + 1,
                section.Name,
                Kind = section.Kind.ToString(),
                RowCount = section.Rows.Count,
                HeaderRows = section.Rows.Count(row => row.IsHeader),
                Columns = section.Columns.Select((name, column) => new { Index = column + 1, Name = name }).ToArray(),
                SuggestedColumn = section.SuggestedColumnIndex + 1
            }).ToArray(),
            SelectedSections = selectedIndices.Select(index =>
            {
                var section = document.Sections[index];
                var column = options.ColumnIndex == -1 ? section.SuggestedColumnIndex : options.ColumnIndex;
                return new
                {
                    Index = index + 1,
                    section.Name,
                    Kind = section.Kind.ToString(),
                    Column = column + 1,
                    ColumnName = column < section.Columns.Count ? section.Columns[column] : "（此区域缺少该列）",
                    RowCount = section.Rows.Count
                };
            }).ToArray()
        }, ReportJsonOptions);
    }

    private static async Task WriteTextAsync(string path, string text, bool overwrite,
        CancellationToken cancellationToken, Action destinationCreated)
    {
        var writePath = overwrite
            ? Path.Combine(Path.GetDirectoryName(path)!, $".dictation-import-{Guid.NewGuid():N}.tmp")
            : path;
        var temporaryCreated = false;
        try
        {
            await using (var stream = new FileStream(writePath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                temporaryCreated = overwrite;
                if (!overwrite)
                {
                    destinationCreated();
                }
                await using var writer = new StreamWriter(stream, Utf8WithoutBom);
                await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (overwrite)
            {
                // Replace the destination only after a complete file is ready;
                // do not truncate an existing file or follow its leaf symlink.
                File.Move(writePath, path, overwrite: true);
                temporaryCreated = false;
                destinationCreated();
            }
        }
        finally
        {
            if (temporaryCreated)
            {
                try { File.Delete(writePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void ExplainOutputState(bool outputCreated, bool outputSaved,
        string? outputPath, string? reportPath, bool reportSaved)
    {
        if (outputSaved)
        {
            Console.Error.WriteLine($"词表文件已保存：{outputPath}。");
            if (reportPath is not null && !reportSaved)
            {
                Console.Error.WriteLine($"附加报告未完整保存，请检查：{reportPath}。");
            }
        }
        else if (outputCreated)
        {
            Console.Error.WriteLine($"输出文件已创建但写入未完成，可能包含部分内容，请检查：{outputPath}。");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            自动默写 — 文档批量提取

            DictationAssistant.App.exe --list-plugins
            DictationAssistant.App.exe --extract INPUT.docx --output OUTPUT.txt [选项]

            --plugin ID          指定已安装插件；默认按插件 ID 排序选取首个匹配者
            --section 1|all      区域编号从 1 开始；默认使用插件推荐区域
                                all 合并所有表格；无表格时合并全部正文区域
            --column auto|N     列编号从 1 开始；默认 auto 使用各区域推荐列
            --deduplicate       按完全相同的文字去重；默认保留重复词条
            --include-headers   包括插件识别出的表头；默认跳过表头
            --keep-numbering    保留开头编号；默认去除带明确分隔符的编号
            --report FILE.json  写出选区、列、计数与提示的核对报告
            --overwrite         允许替换已存在的词表输出或报告
            --help              显示本说明

            路径含空格时请使用双引号。输出文件夹需已存在。
            词表输出必须为 .txt，报告必须为 .json；两者均不能指向输入文件。
            插件目录固定为程序旁的 Plugins；添加或更新插件后重新运行程序。
            退出码：0 成功，1 读取/插件/写入失败或取消，2 参数错误。
            """);
    }

    private sealed class ExtractionArguments
    {
        public string InputFile { get; set; } = string.Empty;
        public string OutputFile { get; set; } = string.Empty;
        public string? ReportFile { get; set; }
        public string? PluginId { get; set; }
        public int? SectionIndex { get; set; }
        public int ColumnIndex { get; set; } = -1;
        public bool Deduplicate { get; set; }
        public bool IncludeHeaders { get; set; }
        public bool KeepNumbering { get; set; }
        public bool Overwrite { get; set; }
    }

    private sealed class CommandLineArgumentException(string message) : Exception(message);
}
