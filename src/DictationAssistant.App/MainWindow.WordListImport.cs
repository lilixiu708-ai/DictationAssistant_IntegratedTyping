// Word-list file selection, import and editor updates, 2026-10-20. AGPL-3.0-or-later.
using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DictationAssistant.App.Importing;

namespace DictationAssistant.App;

public partial class MainWindow
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".lst", ".csv", ".tsv", ".md", ".lrc", ""
    };
    private static readonly FilePickerFileType WordListTextType = new("纯文本词表")
    {
        Patterns = ["*.txt", "*.text", "*.lst", "*.csv", "*.tsv", "*.md", "*.lrc"]
    };
    private bool _isOpeningWordList;
    private bool _isApplyingWordList;
    private string _suggestedWordListFileName = "wordlist.txt";

    private async void ImportDocument_Click(object? sender, RoutedEventArgs e) =>
        await ChooseAndOpenWordListAsync(documentOnly: true);

    private async Task ChooseAndOpenWordListAsync(bool documentOnly)
    {
        await RunFileOpenAsync(async () =>
        {
            await EnsurePluginsLoadedAsync();
            if (documentOnly && _pluginCatalog.SupportedExtensions.Count == 0)
            {
                await ShowFileErrorAsync("尚无可用的文档导入插件。请在“插件 → 插件管理”中查看状态，或打开插件目录安装插件后重启。");
                return null;
            }

            var filters = new List<FilePickerFileType>();
            if (_pluginCatalog.SupportedExtensions.Count > 0)
            {
                filters.Add(new FilePickerFileType(documentOnly ? "可导入的文档" : "词表与可导入文档")
                {
                    Patterns = (documentOnly ? Enumerable.Empty<string>() : WordListTextType.Patterns!)
                        .Concat(_pluginCatalog.SupportedExtensions.Select(extension => "*" + extension)).ToArray()
                });
            }
            if (!documentOnly) filters.Add(WordListTextType);
            filters.Add(FilePickerFileTypes.All);
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = documentOnly ? "从文档提取词表" : "打开词表或导入文档",
                AllowMultiple = false,
                FileTypeFilter = filters
            });
            return files.FirstOrDefault();
        }, documentOnly);
    }

    private async Task RunFileOpenAsync(Func<Task<IStorageFile?>> selectFile, bool documentOnly = false)
    {
        if (_isOpeningWordList || _isApplyingWordList || _isShufflingWordList || WordlistEditor is null)
            return;
        _isOpeningWordList = true;
        try
        {
            var file = await selectFile();
            if (file is null) return;
            await EnsurePluginsLoadedAsync();
            var importers = _pluginCatalog.GetImportersForFile(file.Name);
            if (importers.Count > 0)
            {
                var preview = new WordListImportWindow(file.Name, importers,
                    (plugin, token) => PluginRuntime.InvokeAsync(plugin, "import-document",
                        ct => DocumentImportService.ReadAsync(plugin, file.OpenReadAsync, file.Name, ct), token));
                var selection = await preview.ShowDialog<WordListImportSelection?>(this);
                if (selection is null) return;
                await ApplyWordListTextAsync(selection.Text, selection.Append);
                // The source document is read-only input. Saving the resulting
                // word list must propose a new TXT, never reuse the DOCX handle.
                _currentFile = null;
                _suggestedWordListFileName = Path.GetFileNameWithoutExtension(file.Name) + ".txt";
                var action = selection.Append ? "追加" : "导入";
                ShuffleStatusMessage.Text = $"已从 {file.Name} {action} {ImportPreviewBuilder.CountEntries(selection.Text)} 条。可打乱顺序；Ctrl+Z 可撤销。";
                return;
            }

            if (documentOnly || !TextExtensions.Contains(Path.GetExtension(file.Name)))
            {
                var extension = Path.GetExtension(file.Name);
                throw new InvalidDataException($"没有已加载的 {extension} 导入插件。请在“插件 → 插件管理”中检查；Word 文档需要 Word 词表导入插件，旧版 .doc 请先另存为 .docx。");
            }

            var text = await WordListTextReader.ReadAsync(file.OpenReadAsync, _windowLifetime.Token);
            await ApplyWordListTextAsync(text, append: false);
            _currentFile = file;
            _suggestedWordListFileName = file.Name;
            ShuffleStatusMessage.Text = $"已打开 {file.Name}，共 {ImportPreviewBuilder.CountEntries(text)} 条。";
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"[DocumentImport] Cannot open word list: {ex}");
            if (!_isClosing) await ShowFileErrorAsync($"无法打开或导入文件：{ex.Message}");
        }
        finally
        {
            _isOpeningWordList = false;
        }
    }

    private async Task ApplyWordListTextAsync(string text, bool append, long? expectedRevision = null,
        CancellationToken cancellationToken = default, int? maximumCharacters = null)
    {
        _windowLifetime.Token.ThrowIfCancellationRequested();
        if (_isApplyingWordList || _isShufflingWordList)
            throw new InvalidOperationException("词表正在更新，请稍后重试。");
        _isApplyingWordList = true;
        var wasEnabled = IsEnabled;
        try
        {
            IsEnabled = false;
            if (GetViewModel() is { } vm)
                await vm.DictationPlayer.ResetProgressAsync();
            cancellationToken.ThrowIfCancellationRequested();
            _windowLifetime.Token.ThrowIfCancellationRequested();
            if (expectedRevision is { } expected && expected != _wordListRevision)
                throw new InvalidOperationException("词表已发生变化，请重新读取后再更新。");

            // Snapshot after the player has stopped. Keep the same document
            // instance so the speech source and the editor stay attached.
            var document = WordlistEditor.Document;
            var current = document.Text;
            var next = text;
            if (append && text.Length == 0) next = current;
            else if (append && current.Length > 0)
            {
                var newline = GetExistingNewLine(current);
                var separator = current.EndsWith('\r') || current.EndsWith('\n') ? string.Empty : newline;
                if (maximumCharacters is { } maximum && (long)current.Length + separator.Length + text.Length > maximum)
                    throw new InvalidDataException($"插件更新后的词表不能超过 {maximum:N0} 字符。");
                next = current + separator + text.ReplaceLineEndings(newline);
            }
            if (maximumCharacters is { } limit && next.Length > limit)
                throw new InvalidDataException($"插件更新后的词表不能超过 {limit:N0} 字符。");
            if (!string.Equals(current, next, StringComparison.Ordinal))
            {
                document.BeginUpdate();
                try { document.Replace(0, document.TextLength, next); }
                finally { document.EndUpdate(); }
            }
            WordlistEditor.Select(0, 0);
            WordlistEditor.ScrollToLine(1);
            if (_highlightedLineRenderer is not null) _highlightedLineRenderer.LineNumber = 0;
        }
        finally
        {
            IsEnabled = wasEnabled && !_isClosing;
            _isApplyingWordList = false;
            if (!_isClosing) WordlistEditor.Focus();
        }
    }

    private static string GetExistingNewLine(string text)
    {
        var lf = text.IndexOf('\n');
        if (lf >= 0) return lf > 0 && text[lf - 1] == '\r' ? "\r\n" : "\n";
        return text.Contains('\r') ? "\r" : Environment.NewLine;
    }

}
