// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.Practices.Typing;

internal sealed class TypingPracticeWindow : Window
{
    private readonly IPracticeHost _host;
    private readonly CancellationTokenSource _cancellation;
    private readonly TextBlock _progress = new() { FontSize = 16 };
    private readonly TextBlock _phaseTitle = new()
    {
        Text = "听写打字练习", FontSize = 30, FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly TextBlock _phaseDetail = new()
    {
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.8
    };
    private readonly TextBlock _mask = new()
    {
        FontSize = 29, FontFamily = new FontFamily("Consolas,monospace"),
        HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _countdown = new()
    {
        FontSize = 18, Width = 460, Height = 28,
        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center
    };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _seconds = new() { Width = 80, Text = "10", HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly CheckBox _ignoreCase = new() { Content = "评分时忽略英文大小写", IsChecked = true };
    private readonly Button _startButton = new() { Content = "开始练习", MinWidth = 112 };
    private readonly Button _closeButton = new() { Content = "取消并关闭", MinWidth = 112 };
    private readonly Button _exportButton = new() { Content = "导出成绩 CSV", IsVisible = false };
    private readonly TextBox _answer = new()
    {
        IsVisible = false, FontSize = 25, MinHeight = 52,
        Watermark = "听到完整播报后，在这里打字（Enter 提交）",
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
    private readonly StackPanel _setupPanel = new() { Spacing = 10 };
    private readonly ProgressBar _sessionProgress = new() { Height = 6, Minimum = 0 };
    private readonly ScrollViewer _exerciseScroll;
    private readonly Grid _resultsView = new() { RowDefinitions = new RowDefinitions("Auto,*"), IsVisible = false };
    private readonly ListBox _resultsList = new()
    {
        BorderThickness = new Thickness(0),
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 0.5 }),
        ItemTemplate = new FuncDataTemplate<TypedResult>(static (_, _) => new ResultRow(), supportsRecycling: true)
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _clock = new();
    private readonly List<TypedResult> _records = [];

    private TaskCompletionSource<TypedAnswer>? _pendingAnswer;
    private Task? _sessionTask;
    private Phase _phase = Phase.Setup;
    private int _typingSeconds;
    private int _lastDisplayedSeconds = -1;
    private int _index;
    private bool _closing;
    private bool _allowClose;
    private bool _enterPressed;
    private bool _ignoreCaseSnapshot;
    private bool _csvSaving;

    public TypingPracticeWindow(IPracticeHost host, CancellationToken externalToken)
    {
        _host = host;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        Title = "同步打字听写";
        Width = 750;
        Height = 640;
        MinWidth = 570;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 14 };
        Content = root;

        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(_progress);
        header.Children.Add(_sessionProgress);
        header.Children.Add(_phaseTitle);
        header.Children.Add(_phaseDetail);
        _sessionProgress.Maximum = Math.Max(1, _host.Words.Count);
        root.Children.Add(header);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        footer.Children.Add(_exportButton);
        footer.Children.Add(_closeButton);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Grid.SetRow(_error, 2);
        root.Children.Add(_error);

        var content = new StackPanel { Spacing = 14 };
        _exerciseScroll = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var body = new Grid();
        body.Children.Add(_exerciseScroll);
        body.Children.Add(_resultsView);
        var card = new Border
        {
            Child = body, Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray
        };
        Grid.SetRow(card, 1);
        root.Children.Add(card);
        content.Children.Add(_mask);
        content.Children.Add(_countdown);
        content.Children.Add(_answer);
        content.Children.Add(_setupPanel);

        var resultHeader = new TextBlock
        {
            Text = "逐词核对 · 正确词条与本次输入", FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(10, 0, 10, 8)
        };
        _resultsView.Children.Add(resultHeader);
        // The list owns its scroll viewer and receives a finite viewport from
        // this star row. Wrapping it in the exercise scroll would disable virtualization.
        Grid.SetRow(_resultsList, 1);
        ScrollViewer.SetVerticalScrollBarVisibility(_resultsList, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_resultsList, ScrollBarVisibility.Disabled);
        _resultsList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 8))
            }
        });
        _resultsView.Children.Add(_resultsList);

        var secondsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        secondsRow.Children.Add(new TextBlock { Text = "每词打字时限（秒，1–300）：", VerticalAlignment = VerticalAlignment.Center });
        secondsRow.Children.Add(_seconds);
        _setupPanel.Children.Add(new TextBlock { Text = $"词表：{_host.Words.Count} 条，每词播报 {_host.TimesPerWord} 次，重复间隔：{_host.IntervalDescription} 秒。", TextWrapping = TextWrapping.Wrap });
        _setupPanel.Children.Add(new TextBlock { Text = "同一个词的最后一遍语音完全播放完毕后才显示下划线并开始计时；其间不允许输入，全部词条完成前不显示答案或成绩。", TextWrapping = TextWrapping.Wrap });
        _setupPanel.Children.Add(secondsRow);
        _setupPanel.Children.Add(_ignoreCase);
        _setupPanel.Children.Add(_startButton);
        _phaseDetail.Text = "设定作答时限后开始。Enter 可提前提交；到时自动记录当前输入。";
        _progress.Text = $"准备就绪 · 0 / {_host.Words.Count}";
        _closeButton.Click += async (_, _) => await CancelAndCloseAsync();
        _startButton.Click += (_, _) => StartPractice();
        _exportButton.Click += async (_, _) => await ExportCsvAsync();
        // Capture Enter/KeyUp even when the textbox itself handles the key.
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(InputElement.KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _timer.Tick += OnTimerTick;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _resultsList.ItemsSource = null;
            // An in-flight CSV export still needs the records until it finishes.
            _cancellation.Dispose();
        };
        RestorePreferences();
    }

    private void StartPractice()
    {
        if (_sessionTask is not null) return;
        if (!int.TryParse(_seconds.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var secs)
            || secs is < 1 or > 300)
        {
            _error.Text = "请输入 1 到 300 之间的整数秒数。";
            return;
        }

        _typingSeconds = secs;
        _ignoreCaseSnapshot = _ignoreCase.IsChecked == true;
        _error.Text = string.Empty;
        SavePreferences();
        _setupPanel.IsVisible = false;
        _startButton.IsEnabled = false;
        _closeButton.Content = "结束本轮（不评分）";
        _sessionTask = RunSessionAsync(_cancellation.Token);
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        try
        {
            for (_index = 0; _index < _host.Words.Count; _index++)
            {
                ct.ThrowIfCancellationRequested();
                var word = _host.Words[_index];
                // The entire sequence (all repetitions plus every between-repeat
                // interval) remains in the "playing" phase. The awaited method
                // returns only when the last device playback completes.
                await RepetitionPlayback.PlayAsync(word, Math.Clamp(_host.TimesPerWord, 1, 100),
                    _host.SpeakOnceAsync, _host.GetRepeatInterval,
                    (repeat, maxRepeat, waiting) => ShowPlaying(repeat, maxRepeat, waiting), ct);
                ct.ThrowIfCancellationRequested();
                var answer = await AwaitTypingAsync(word, ct);
                // Only record here. Do not compare and do not show correct text.
                _records.Add(new TypedResult(_index + 1, word, answer.Value,
                    answer.TimedOut, answer.Elapsed.TotalSeconds));
                _sessionProgress.Value = _records.Count;
            }
            ct.ThrowIfCancellationRequested();
            _phaseTitle.Text = "正在汇总成绩";
            var correct = await Task.Run(() => ScoreRecords(ct), ct);
            ct.ThrowIfCancellationRequested();
            ShowFinalResults(correct);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested || ct.IsCancellationRequested)
        {
            // Interrupted sessions do not reveal or score partially answered items.
        }
        catch (Exception ex)
        {
            _phase = Phase.Error;
            _timer.Stop();
            _answer.IsVisible = false;
            _mask.IsVisible = false;
            _phaseTitle.Text = "播放或练习失败";
            _phaseDetail.Text = "本轮未评分，请确认语音引擎和音频输出后重试。";
            _error.Text = ex.Message;
            _closeButton.Content = "关闭";
        }
    }

    private void ShowPlaying(int repeat, int maxRepeat, bool waiting)
    {
        _phase = Phase.Playing;
        _progress.Text = $"第 {_index + 1} / {_host.Words.Count} 词";
        _phaseTitle.Text = "播放中";
        _phaseDetail.Text = waiting
            ? $"第 {repeat} / {maxRepeat} 遍已结束；正在等待下一遍播报…"
            : $"正在完整播放第 {repeat} / {maxRepeat} 遍";
        _mask.IsVisible = false;
        _answer.IsVisible = false;
        _countdown.Text = "请仔细听，播报全部结束后才能输入";
    }

    private async Task<TypedAnswer> AwaitTypingAsync(string word, CancellationToken ct)
    {
        _phase = Phase.Typing;
        _phaseTitle.Text = "请打字";
        _phaseDetail.Text = "完整播放已结束。输入后按 Enter 提交，或等待倒计时结束。";
        _mask.Text = TypingEvaluation.Mask(word);
        _mask.IsVisible = true;
        _answer.Text = string.Empty;
        _answer.IsVisible = true;
        _lastDisplayedSeconds = -1;
        _clock.Restart();
        var pending = new TaskCompletionSource<TypedAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAnswer = pending;
        using var registration = ct.Register(() => pending.TrySetCanceled(ct));
        OnTimerTick(this, EventArgs.Empty);
        _timer.Start();
        Dispatcher.UIThread.Post(() => _answer.Focus(), DispatcherPriority.Input);
        try
        {
            return await pending.Task;
        }
        finally
        {
            _timer.Stop();
            _clock.Stop();
            if (ReferenceEquals(_pendingAnswer, pending)) _pendingAnswer = null;
            _answer.IsVisible = false;
            _mask.IsVisible = false;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = _phase == Phase.Typing;
        // A held Enter key must not submit a newly displayed next question.
        if (_enterPressed) return;
        _enterPressed = true;
        if (_phase == Phase.Typing) CompleteAnswer(timedOut: false);
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _enterPressed = false;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_phase != Phase.Typing || _pendingAnswer is null) return;
        var left = Math.Max(0, _typingSeconds - _clock.Elapsed.TotalSeconds);
        var displayedSeconds = (int)Math.Ceiling(left);
        // Keep deadline checks frequent without rebuilding text during every input tick.
        if (displayedSeconds != _lastDisplayedSeconds)
        {
            _lastDisplayedSeconds = displayedSeconds;
            _countdown.Text = $"剩余时间：{displayedSeconds} 秒";
        }
        if (left <= 0) CompleteAnswer(timedOut: true);
    }

    private void CompleteAnswer(bool timedOut)
    {
        if (_phase != Phase.Typing || _pendingAnswer is not { } pending) return;
        var elapsed = _clock.Elapsed;
        timedOut |= elapsed >= TimeSpan.FromSeconds(_typingSeconds);
        if (pending.TrySetResult(new TypedAnswer(_answer.Text ?? string.Empty, timedOut, elapsed)))
        {
            _phase = Phase.Committing;
            _timer.Stop();
            _answer.IsVisible = false;
            _mask.IsVisible = false;
            _phaseTitle.Text = "已记录";
            _phaseDetail.Text = "准备下一词…";
            _countdown.Text = string.Empty;
        }
    }

    private int ScoreRecords(CancellationToken ct)
    {
        var correct = 0;
        foreach (var record in _records)
        {
            ct.ThrowIfCancellationRequested();
            record.Correct = TypingEvaluation.IsCorrect(record.Expected, record.Entered, _ignoreCaseSnapshot);
            if (record.Correct) correct++;
        }
        return correct;
    }

    private void ShowFinalResults(int correct)
    {
        _phase = Phase.Completed;
        _timer.Stop();
        var count = _records.Count;
        var wrong = count - correct;
        _phaseTitle.Text = $"正确率 {TypingEvaluation.Accuracy(correct, count):F1}%";
        _phaseDetail.Text = $"完成 {count} 词 · 正确 {correct} · 错误或漏答 {wrong}。已在整轮结束后统一评分。";
        _progress.Text = $"已完成 · {count} / {_host.Words.Count}";
        _countdown.Text = string.Empty;
        _answer.IsVisible = false;
        _mask.IsVisible = false;
        _exerciseScroll.IsVisible = false;
        _resultsList.ItemsSource = _records;
        _resultsView.IsVisible = true;
        _closeButton.Content = "关闭";
        _exportButton.IsVisible = true;
    }

    private async Task ExportCsvAsync()
    {
        if (_phase != Phase.Completed || _csvSaving) return;
        _csvSaving = true;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出同步打字成绩",
                SuggestedFileName = $"TypingPractice_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV 表格") { Patterns = ["*.csv"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            await writer.WriteLineAsync("序号,正确词条,输入内容,是否正确,是否超时,耗时秒");
            foreach (var row in _records)
            {
                await writer.WriteLineAsync(string.Join(",", new[]
                {
                    row.Index.ToString(CultureInfo.InvariantCulture),
                    TypingEvaluation.CsvCell(row.Expected), TypingEvaluation.CsvCell(row.Entered),
                    row.Correct ? "正确" : "错误", row.TimedOut ? "是" : "否",
                    row.ElapsedSeconds.ToString("F2", CultureInfo.InvariantCulture)
                }));
            }
            await writer.FlushAsync();
            _error.Text = "已导出成绩 CSV。";
        }
        catch (Exception ex)
        {
            _error.Text = $"保存 CSV 失败：{ex.Message}";
        }
        finally { _csvSaving = false; }
    }

    private async Task CancelAndCloseAsync()
    {
        if (_closing) return;
        _closing = true;
        _cancellation.Cancel();
        _pendingAnswer?.TrySetCanceled();
        if (_sessionTask is not null)
        {
            try { await _sessionTask; }
            catch (OperationCanceledException) { }
        }
        _allowClose = true;
        Close();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || _sessionTask is null || _sessionTask.IsCompleted) return;
        e.Cancel = true;
        await CancelAndCloseAsync();
    }

    private static string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DictationAssistant", "Plugins", "SynchronousTyping", "settings.json");

    private void RestorePreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return;
            var saved = JsonSerializer.Deserialize<PracticePreferences>(File.ReadAllText(PreferencesPath));
            if (saved is null) return;
            _seconds.Text = Math.Clamp(saved.TypingSeconds, 1, 300).ToString(CultureInfo.InvariantCulture);
            _ignoreCase.IsChecked = saved.IgnoreCase;
        }
        catch (Exception) { /* Settings failure must never prevent launching an exercise. */ }
    }

    private void SavePreferences()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(new PracticePreferences
            {
                TypingSeconds = _typingSeconds, IgnoreCase = _ignoreCaseSnapshot
            }));
        }
        catch (Exception) { /* The session still works if user config is read-only. */ }
    }

    private sealed class PracticePreferences
    {
        public PracticePreferences() { }
        public int TypingSeconds { get; set; } = 10;
        public bool IgnoreCase { get; set; } = true;
    }

    private sealed record TypedAnswer(string Value, bool TimedOut, TimeSpan Elapsed);
    private sealed record TypedResult(int Index, string Expected, string Entered, bool TimedOut, double ElapsedSeconds)
    {
        public bool Correct { get; set; }
    }

    private sealed class ResultRow : Grid
    {
        private readonly TextBlock _number = new();
        private readonly TextBlock _expected = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _entered = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

        public ResultRow()
        {
            ColumnDefinitions = new ColumnDefinitions("40,*,64");
            RowDefinitions = new RowDefinitions("Auto,Auto");
            ColumnSpacing = 12;
            RowSpacing = 4;
            Grid.SetRowSpan(_number, 2);
            Grid.SetColumn(_expected, 1);
            Grid.SetColumn(_entered, 1);
            Grid.SetRow(_entered, 1);
            Grid.SetColumn(_status, 2);
            Grid.SetRowSpan(_status, 2);
            Children.Add(_number);
            Children.Add(_expected);
            Children.Add(_entered);
            Children.Add(_status);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is not TypedResult record)
            {
                _number.Text = _expected.Text = _entered.Text = _status.Text = null;
                return;
            }

            // Virtualized rows are recycled; update every field for the new item.
            // Format only visible rows instead of retaining one string/control per word.
            _number.Text = record.Index.ToString(CultureInfo.InvariantCulture);
            _expected.Text = "正确：" + record.Expected;
            _entered.Text = "输入：" + (string.IsNullOrEmpty(record.Entered) ? "（空白）" : record.Entered);
            _status.Text = record.Correct
                ? (record.TimedOut ? "✓ 正确\n到时" : "✓ 正确")
                : (record.TimedOut ? "✗ 超时" : "✗ 错误");
            _status.Foreground = record.Correct ? Brushes.SeaGreen : Brushes.IndianRed;
        }
    }
    private enum Phase { Setup, Playing, Typing, Committing, Completed, Error }
}
