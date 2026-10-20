// Sideloaded practice plugin integration; no exercise implementation in the host.
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Importing;
using DictationAssistant.Plugin.Abstractions;
using System.Diagnostics;
using Avalonia.Threading;

namespace DictationAssistant.App;

public partial class MainWindow
{
    private bool _isPracticing;

    private void InitializePractice()
    {
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new AsyncRelayCommand(StartTypingPracticeAsync)
        });
    }

    private async void TypingPractice_Click(object? sender, RoutedEventArgs e)
    {
        await StartTypingPracticeAsync();
    }

    private Task StartTypingPracticeAsync() => StartPracticeAsync("synchronous-typing");

    private async Task StartPracticeAsync(string pluginId)
    {
        if (_isPracticing || _isOpeningWordList || _isApplyingWordList || _isShufflingWordList ||
            WordlistEditor is null || GetViewModel() is not { } vm)
            return;

        _isPracticing = true;
        try
        {
            await EnsurePluginsLoadedAsync();
            var entry = _pluginCatalog.GetPracticePlugin(pluginId);
            if (entry?.PracticePlugin is not { } plugin)
            {
                await ShowFileErrorAsync($"未加载练习插件 {pluginId}。请在插件管理中查看状态，安装插件后重启。");
                return;
            }

            ShuffleStatusMessage.Text = "正在读取练习词表…";
            // Work from one immutable editor snapshot. Count source lines independently
            // from parsing, and keep both passes off the input thread.
            var (words, expectedCount) = await ReadPracticeWordsAsync(_windowLifetime.Token);
            if (words.Count == 0 && plugin is not IPracticeSourceSelectionPlugin { CanStartWithoutWords: true })
            {
                await ShowFileErrorAsync("词表为空：请先导入 Word 或输入词条，每行一个。");
                return;
            }

            // Stop the normal player and await its cancellation before opening
            // a modal practice window. Main-window controls cannot interfere.
            await vm.DictationPlayer.ResetProgressAsync();
            var player = vm.DictationPlayer;
            var calculator = player.WaitingTimeCalculator;
            var host = new PracticeHost(
                this, words, expectedCount,
                Math.Clamp(player.TimesPerWord, 1, 100), vm.IntervalExpression,
                word => TimeSpan.FromSeconds(Math.Clamp(
                    calculator?.CalculateWaitingTime(word) ?? 3, 0, 3600)),
                (word, token) => player.SpeakPracticeWordAsync(word, token),
                OpenPracticeDocumentAsync);

            ShuffleStatusMessage.Text = words.Count == 0
                ? "背诵练习：请在准备界面选择文档和练习内容。"
                : $"背诵练习：当前词表 {words.Count} 条，可在准备界面选择提示与作答方式。";
            await PluginRuntime.InvokeAsync(entry, "practice", async token =>
            {
                var callback = PluginRuntime.CaptureInvocation(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return plugin.RunAsync(host, token);
                });
                // Keep the runtime lease until the dispatched practice task completes.
                var practiceTask = await Dispatcher.UIThread.InvokeAsync(callback, DispatcherPriority.Normal, token);
                await practiceTask;
            }, _windowLifetime.Token);
            ShuffleStatusMessage.Text = $"{entry.DisplayName}已结束。";
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SynchronousTyping] Plugin failed: {ex}");
            if (!_isClosing) await ShowFileErrorAsync($"练习无法运行：{ex.Message}");
        }
        finally
        {
            try
            {
                if (GetViewModel() is { } currentVm)
                    await currentVm.DictationPlayer.ResetProgressAsync();
            }
            finally { _isPracticing = false; }
        }
    }

    private async Task<PracticeDocument?> OpenPracticeDocumentAsync(object ownerWindow, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ownerWindow is not Window owner)
            throw new InvalidOperationException("当前宿主不支持这个文档选择窗口。");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _windowLifetime.Token);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        if (_pluginCatalog.SupportedExtensions.Count == 0)
            throw new InvalidOperationException("尚无可用的文档导入插件，请安装 Word / Excel 导入插件后重启。");

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择背诵练习文档",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("可导入的背诵文档")
                {
                    Patterns = _pluginCatalog.SupportedExtensions.Select(extension => "*" + extension).ToArray()
                },
                FilePickerFileTypes.All
            ]
        });
        token.ThrowIfCancellationRequested();
        using var file = files.FirstOrDefault();
        if (file is null) return null;
        var fileName = file.Name;
        var importer = _pluginCatalog.GetImportersForFile(fileName).FirstOrDefault()
            ?? throw new InvalidDataException("这个文件没有可用的文档导入插件，请选择 Word 或 Excel 文档。");
        var document = await PluginRuntime.InvokeAsync(importer, "practice-document-import",
            ct => DocumentImportService.ReadAsync(importer, file.OpenReadAsync, fileName, ct,
                preserveNotation: true), token);
        token.ThrowIfCancellationRequested();
        return new PracticeDocument(fileName, document);
    }

    private Task<(IReadOnlyList<string> Words, int ExpectedCount)> ReadPracticeWordsAsync(CancellationToken ct)
    {
        // Do not retain the original full text in the modal exercise's async state.
        var text = WordlistEditor.Text;
        return Task.Run(() => ReadPracticeWords(text, ct), ct);
    }

    private static (IReadOnlyList<string> Words, int ExpectedCount) ReadPracticeWords(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = text.ReplaceLineEndings("\n");
        var expected = 0;
        foreach (var line in normalized.AsSpan().EnumerateLines())
        {
            ct.ThrowIfCancellationRequested();
            if (!line.Trim().IsEmpty) expected++;
        }
        var words = normalized.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        ct.ThrowIfCancellationRequested();
        return (Array.AsReadOnly(words), expected);
    }

    private sealed class PracticeHost : IPracticeHost, IPracticeWordListMetadata, IPracticeDocumentHost
    {
        private readonly Func<string, TimeSpan> _getInterval;
        private readonly Func<string, CancellationToken, Task> _speak;
        private readonly Func<object, CancellationToken, Task<PracticeDocument?>> _openDocument;

        public PracticeHost(object owner, IReadOnlyList<string> words, int expectedWordCount, int repeats,
            string intervalDescription, Func<string, TimeSpan> getInterval,
            Func<string, CancellationToken, Task> speak,
            Func<object, CancellationToken, Task<PracticeDocument?>> openDocument)
        {
            OwnerWindow = owner;
            Words = words;
            ExpectedWordCount = expectedWordCount;
            TimesPerWord = repeats;
            IntervalDescription = intervalDescription;
            _getInterval = getInterval;
            _speak = speak;
            _openDocument = openDocument;
        }

        public object OwnerWindow { get; }
        public IReadOnlyList<string> Words { get; }
        public int ExpectedWordCount { get; }
        public int TimesPerWord { get; }
        public string IntervalDescription { get; }
        public TimeSpan GetRepeatInterval(string word) => _getInterval(word);
        public Task<PracticeDocument?> OpenDocumentAsync(object ownerWindow, CancellationToken cancellationToken) =>
            _openDocument(ownerWindow, cancellationToken);
        public Task SpeakOnceAsync(string word, CancellationToken cancellationToken) =>
            // Cached synthesis and local audio can complete synchronously, so
            // ConfigureAwait(false) alone cannot keep decoding/device setup
            // off the UI thread when the user submits an answer.
            Task.Run(() => _speak(word, cancellationToken), cancellationToken);
    }
}
