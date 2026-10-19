// Sideloaded document import workflow, 2026-10-19. AGPL-3.0-or-later.
using System.Diagnostics;
using System.Text;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DictationAssistant.App.Importing;
using DictationAssistant.App.Plugins;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

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
    private readonly PluginCatalog _pluginCatalog = new(Path.Combine(AppContext.BaseDirectory, "Plugins"));
    private readonly CancellationTokenSource _windowLifetime = new();
    private DispatcherTimer? _pluginWordListTimer;
    private long _wordListRevision;
    private bool _isClosing;
    private bool _canClose;
    public PluginRuntime PluginRuntime { get; private set; } = null!;
    private bool _isOpeningWordList;
    private bool _isApplyingWordList;
    private string _suggestedWordListFileName = "wordlist.txt";

    private void InitializePlugins()
    {
        PluginRuntime = new PluginRuntime(_pluginCatalog,
            entry => new WindowPluginContext(this, entry), _windowLifetime.Token);
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.I, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new AsyncRelayCommand(() => ChooseAndOpenWordListAsync(documentOnly: true))
        });
        Opened += async (_, _) =>
        {
            WordlistEditor.Document.Changed += (_, _) =>
            {
                _wordListRevision++;
                if (_isClosing || !_pluginCatalog.Entries.Any(entry =>
                        entry.IsInitialized && entry.WordListObserver is not null)) return;
                _pluginWordListTimer ??= CreatePluginWordListTimer();
                _pluginWordListTimer.Stop();
                _pluginWordListTimer.Start();
            };
            try
            {
                await EnsurePluginsLoadedAsync();
                RebuildPluginMenus();
                if (!_isClosing) NotifyPluginsOfWordList();
            }
            catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Plugins] Initialization failed: {ex}");
                if (!_isClosing) await ShowFileErrorAsync($"插件初始化失败：{ex.Message}");
            }
        };
        Closing += MainWindow_Closing;
    }

    private Task EnsurePluginsLoadedAsync() => PluginRuntime.InitializeAsync(_windowLifetime.Token);

    private DispatcherTimer CreatePluginWordListTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); NotifyPluginsOfWordList(); };
        return timer;
    }

    private void NotifyPluginsOfWordList()
    {
        if (_isClosing || !_pluginCatalog.Entries.Any(entry => entry.IsInitialized && entry.WordListObserver is not null)) return;
        PluginRuntime.NotifyWordListChanged(GetPluginWordListSnapshot());
    }

    private DictationAssistant.Plugin.Abstractions.PluginWordListSnapshot GetPluginWordListSnapshot() =>
        new(WordlistEditor.Text, WordlistEditor.SelectedText, _wordListRevision);

    private void RebuildPluginMenus()
    {
        PluginCommandsMenu.Items.Clear();
        PluginPracticesMenu.Items.Clear();
        foreach (var entry in _pluginCatalog.Entries.Where(entry => entry.IsLoaded && entry.IsInitialized))
        {
            foreach (var command in entry.Commands)
            {
                var item = new MenuItem { Header = $"{entry.DisplayName} · {command.Name}" };
                ToolTip.SetTip(item, command.Description);
                item.Click += async (_, _) => await RunPluginCommandAsync(entry, command.Id);
                PluginCommandsMenu.Items.Add(item);
            }
            if (entry.PracticePlugin is not null)
            {
                var item = new MenuItem { Header = entry.DisplayName };
                item.Click += async (_, _) => await StartPracticeAsync(entry.Manifest!.Id);
                PluginPracticesMenu.Items.Add(item);
            }
        }
        PluginCommandsMenu.IsEnabled = PluginCommandsMenu.Items.Count > 0;
        PluginPracticesMenu.IsEnabled = PluginPracticesMenu.Items.Count > 0;
    }

    private async Task RunPluginCommandAsync(PluginEntry entry, string commandId)
    {
        if (_isClosing || _isPracticing) return;
        try
        {
            await PluginRuntime.InvokeAsync(entry, "command:" + commandId,
                token => entry.CommandPlugin!.ExecuteAsync(commandId, PluginRuntime.GetContext(entry), token),
                _windowLifetime.Token);
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Plugins] Command failed: {ex}");
            if (!_isClosing) await ShowFileErrorAsync($"插件 {entry.DisplayName} 执行失败：{ex.Message}");
        }
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_isClosing) return;
        _isClosing = true;
        IsEnabled = false;
        _pluginWordListTimer?.Stop();
        try
        {
            try { _windowLifetime.Cancel(); }
            catch (Exception ex) { Trace.WriteLine($"[Shutdown] Cancellation failed: {ex}"); }
            try
            {
                if (GetViewModel() is { } vm) await vm.ShutdownAsync();
            }
            finally { await PluginRuntime.ShutdownAsync(); }
        }
        catch (Exception ex) { Trace.WriteLine($"[Shutdown] Cleanup failed: {ex}"); }
        finally
        {
            _canClose = true;
            _windowLifetime.Dispose();
            Close();
        }
    }

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

            await using var stream = await file.OpenReadAsync();
            if (stream.CanSeek && stream.Length > DocumentImportService.MaximumInputBytes)
                throw new InvalidDataException("词表超过 64 MiB，请拆分后再打开。");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync();
            if (text.Contains('\0'))
                throw new InvalidDataException("这个文件包含二进制内容，请使用对应的文档导入插件。");
            await ApplyWordListTextAsync(text, append: false);
            _currentFile = file;
            _suggestedWordListFileName = file.Name;
            ShuffleStatusMessage.Text = $"已打开 {file.Name}，共 {ImportPreviewBuilder.CountEntries(text)} 条。";
        }
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

    private async void PluginManager_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await EnsurePluginsLoadedAsync();
            await new PluginManagerWindow(PluginRuntime, OpenPluginFolder).ShowDialog(this);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Plugins] Manager failed: {ex}");
            if (!_isClosing) await ShowFileErrorAsync($"无法打开插件管理：{ex.Message}");
        }
    }

    private void OpenPluginFolder()
    {
        Directory.CreateDirectory(_pluginCatalog.RootDirectory);
        Process.Start(new ProcessStartInfo(_pluginCatalog.RootDirectory) { UseShellExecute = true });
    }

    private async void OpenPluginFolder_Click(object? sender, RoutedEventArgs e)
    {
        try { OpenPluginFolder(); }
        catch (Exception ex) { await ShowFileErrorAsync($"无法打开插件目录：{ex.Message}"); }
    }

    private async Task ShowFileErrorAsync(string message)
    {
        if (_isClosing) return;
        var box = MessageBoxManager.GetMessageBoxStandard("自动默写", message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        await box.ShowWindowDialogAsync(this);
    }
}
