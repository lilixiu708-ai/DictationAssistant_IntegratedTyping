// Sideloaded practice plugin integration; no exercise implementation in the host.
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using DictationAssistant.App.Abstractions;
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
            if (words.Count == 0)
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
                (word, token) => player.SpeakPracticeWordAsync(word, token));

            ShuffleStatusMessage.Text = $"同步打字练习：{words.Count} 条，{host.TimesPerWord} 次/词。";
            await PluginRuntime.InvokeAsync(entry, "practice", async token =>
            {
                var callback = PluginRuntime.CaptureInvocation(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return plugin.RunAsync(host, token);
                });
                await Dispatcher.UIThread.InvokeAsync(callback, DispatcherPriority.Normal, token);
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

    private sealed class PracticeHost : IPracticeHost, IPracticeWordListMetadata
    {
        private readonly Func<string, TimeSpan> _getInterval;
        private readonly Func<string, CancellationToken, Task> _speak;

        public PracticeHost(object owner, IReadOnlyList<string> words, int expectedWordCount, int repeats,
            string intervalDescription, Func<string, TimeSpan> getInterval,
            Func<string, CancellationToken, Task> speak)
        {
            OwnerWindow = owner;
            Words = words;
            ExpectedWordCount = expectedWordCount;
            TimesPerWord = repeats;
            IntervalDescription = intervalDescription;
            _getInterval = getInterval;
            _speak = speak;
        }

        public object OwnerWindow { get; }
        public IReadOnlyList<string> Words { get; }
        public int ExpectedWordCount { get; }
        public int TimesPerWord { get; }
        public string IntervalDescription { get; }
        public TimeSpan GetRepeatInterval(string word) => _getInterval(word);
        public Task SpeakOnceAsync(string word, CancellationToken cancellationToken) =>
            // Cached synthesis and local audio can complete synchronously, so
            // ConfigureAwait(false) alone cannot keep decoding/device setup
            // off the UI thread when the user submits an answer.
            Task.Run(() => _speak(word, cancellationToken), cancellationToken);
    }
}
