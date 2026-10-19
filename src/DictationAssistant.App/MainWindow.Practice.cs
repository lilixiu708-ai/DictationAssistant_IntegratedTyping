// Sideloaded practice plugin integration; no exercise implementation in the host.
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using DictationAssistant.App.Abstractions;
using DictationAssistant.Plugin.Abstractions;
using System.Diagnostics;

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

    private async Task StartTypingPracticeAsync()
    {
        if (_isPracticing || _isOpeningWordList || _isApplyingWordList || _isShufflingWordList ||
            WordlistEditor is null || GetViewModel() is not { } vm)
            return;

        _isPracticing = true;
        try
        {
            await EnsurePluginsLoadedAsync();
            var entry = _pluginCatalog.GetPracticePlugin("synchronous-typing");
            if (entry?.PracticePlugin is not { } plugin)
            {
                await ShowFileErrorAsync("尚未加载同步打字插件。请将 Plugins/SynchronousTyping 完整文件夹放在程序旁并重启；可在插件管理中查看错误原因。");
                return;
            }

            var words = WordlistEditor.Text.ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
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
                this, Array.AsReadOnly(words),
                Math.Clamp(player.TimesPerWord, 1, 100), vm.IntervalExpression,
                word => TimeSpan.FromSeconds(Math.Clamp(
                    calculator?.CalculateWaitingTime(word) ?? 3, 0, 3600)),
                (word, token) => player.SpeakPracticeWordAsync(word, token));

            ShuffleStatusMessage.Text = $"同步打字练习：{words.Length} 条，{host.TimesPerWord} 次/词。";
            await plugin.RunAsync(host, CancellationToken.None);
            ShuffleStatusMessage.Text = "同步打字练习已结束；原词表保持不变。";
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SynchronousTyping] Plugin failed: {ex}");
            await ShowFileErrorAsync($"同步打字练习无法运行：{ex.Message}");
        }
        finally
        {
            if (GetViewModel() is { } currentVm)
                await currentVm.DictationPlayer.ResetProgressAsync();
            _isPracticing = false;
        }
    }

    private sealed class PracticeHost : IPracticeHost
    {
        private readonly Func<string, TimeSpan> _getInterval;
        private readonly Func<string, CancellationToken, Task> _speak;

        public PracticeHost(object owner, IReadOnlyList<string> words, int repeats,
            string intervalDescription, Func<string, TimeSpan> getInterval,
            Func<string, CancellationToken, Task> speak)
        {
            OwnerWindow = owner;
            Words = words;
            TimesPerWord = repeats;
            IntervalDescription = intervalDescription;
            _getInterval = getInterval;
            _speak = speak;
        }

        public object OwnerWindow { get; }
        public IReadOnlyList<string> Words { get; }
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
