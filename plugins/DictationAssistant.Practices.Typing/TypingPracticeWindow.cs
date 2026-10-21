// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-21: freeze confirmed Chinese alternatives before offline practice.
using System.Diagnostics;
using System.Globalization;
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
    private readonly PracticePlan _plan;
    private readonly TranslationEngineSession _translationEngines;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellationRegistration;
    private readonly TextBlock _progress = new() { FontSize = 16, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _phaseTitle = new()
    {
        Text = "背诵练习", FontSize = 30, FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly TextBlock _phaseDetail = new()
    {
        FontSize = 18, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.8
    };
    private readonly TextBlock _countdown = new()
    {
        FontSize = 26, MaxWidth = 460, MinHeight = 36, IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _seconds = new() { Width = 80, Text = "10", HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly CheckBox _ignoreCase = new()
    {
        Content = "忽略英文大小写", IsChecked = true, FontSize = 18,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly CheckBox _showTranslation = new()
    {
        Content = "显示提示", IsChecked = false, FontSize = 18,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly TextBlock _translationHint = new()
    {
        FontSize = 22, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false
    };
    private readonly TextBlock _questionPrompt = new()
    {
        FontSize = 34, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        IsVisible = false
    };
    private readonly Button _translationsButton = new()
    {
        Content = "编辑提示", MinWidth = 112, IsEnabled = false,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly CheckBox _useAcceptedAnswers = new()
    {
        Content = "使用已确认的中文答案", IsChecked = true, FontSize = 18,
        HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false
    };
    private readonly Button _acceptedAnswersButton = new()
    {
        Content = "可接受答案…", MinWidth = 132, IsEnabled = false, IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly Button _startButton = new()
    {
        Content = "开始练习", MinWidth = 112, FontSize = 18,
        IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly Button _settleButton = new() { Content = "结算成绩", MinWidth = 112, IsVisible = false };
    private readonly Button _closeButton = new() { Content = "关闭", MinWidth = 112 };
    private readonly Button _exportButton = new() { Content = "导出成绩 CSV", IsVisible = false };
    private readonly TextBox _answer = new()
    {
        IsVisible = false, FontSize = 25, MinHeight = 52,
        Watermark = "听到完整播报后，在这里打字（Enter 提交）",
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
    private readonly StackPanel _setupPanel = new() { Spacing = 18 };
    private readonly TextBlock _wordListSummary = new()
    {
        FontSize = 18, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
    };
    private readonly ProgressBar _sessionProgress = new() { Height = 6, Minimum = 0, IsVisible = false };
    private readonly ScrollViewer _exerciseScroll;
    private readonly Grid _resultsView = new() { RowDefinitions = new RowDefinitions("Auto,*"), IsVisible = false };
    private readonly ListBox _resultsList = new()
    {
        BorderThickness = new Thickness(0),
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 0.5 }),
        ItemTemplate = new FuncDataTemplate<TypedResult>(static (_, _) => new TypingResultRow(), supportsRecycling: true)
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _clock = new();
    private readonly List<TypedResult> _records = [];

    private TaskCompletionSource<TypedAnswer>? _pendingAnswer;
    private Task? _readTask;
    private Task? _sessionTask;
    private Task? _settlementTask;
    private Task? _translationTask;
    private Task? _acceptedAnswersTask;
    private CancellationTokenSource? _sessionCancellation;
    private Dictionary<string, string> _translations = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> _acceptedAnswers =
        new Dictionary<AnswerIdentity, IReadOnlyList<string>>();
    private IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> _roundAcceptedAnswers =
        new Dictionary<AnswerIdentity, IReadOnlyList<string>>();
    private IReadOnlyList<PracticeQuestion> _questions = Array.Empty<PracticeQuestion>();
    private IReadOnlyList<string> _words = Array.Empty<string>();
    private int _expectedWordCount;
    private Phase _phase = Phase.Setup;
    private int _typingSeconds;
    private int _lastDisplayedSeconds = -1;
    private int _index;
    private bool _closing;
    private bool _allowClose;
    private bool _enterPressed;
    private bool _ignoreCaseSnapshot;
    private bool _useAcceptedAnswersSnapshot;
    private bool _acceptedAnswersLoaded;
    private bool _csvSaving;
    private bool _settlementRequested;

    public TypingPracticeWindow(IPracticeHost host, CancellationToken externalToken,
        TranslationEngineSession translationEngines, PracticePlan plan)
    {
        _host = host;
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _translationEngines = translationEngines;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        Title = "背诵练习";
        Width = 750;
        Height = 640;
        MinWidth = 570;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Let our Closing handler cancel and await work before closing owned dialogs.
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;

        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 14 };
        Content = root;

        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(_progress);
        header.Children.Add(_sessionProgress);
        header.Children.Add(_phaseTitle);
        header.Children.Add(_phaseDetail);
        root.Children.Add(header);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        footer.Children.Add(_exportButton);
        footer.Children.Add(_settleButton);
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
        content.Children.Add(_showTranslation);
        content.Children.Add(_translationHint);
        content.Children.Add(_questionPrompt);
        content.Children.Add(_countdown);
        content.Children.Add(_answer);
        content.Children.Add(_setupPanel);

        var resultHeader = new TextBlock
        {
            Text = "提示、答案和你的输入", FontWeight = FontWeight.SemiBold,
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
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        secondsRow.Children.Add(new TextBlock
        {
            Text = "每题用时（1–300 秒）：", FontSize = 18, VerticalAlignment = VerticalAlignment.Center
        });
        secondsRow.Children.Add(_seconds);
        _setupPanel.Children.Add(_wordListSummary);
        _setupPanel.Children.Add(new TextBlock
        {
            Text = (_plan.PlayAudio ? "听完播报后开始答题和计时。"
                : "显示题目后开始计时，不播放音频。")
                + "\n可以中途结算"
                + (_plan.ChemicalAnswers ? "\n化学式可写成 Na^+、SO4^2-，也可用上下标；请注意元素大小写。" : string.Empty),
            FontSize = 18, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
        });
        _setupPanel.Children.Add(secondsRow);
        _setupPanel.Children.Add(_ignoreCase);
        _setupPanel.Children.Add(_translationsButton);
        _setupPanel.Children.Add(_useAcceptedAnswers);
        _setupPanel.Children.Add(_acceptedAnswersButton);
        _setupPanel.Children.Add(_startButton);
        _phaseDetail.Text = "设置每题用时后开始。按 Enter 提交，到时自动提交。";
        _answer.Watermark = _plan.ChemicalAnswers ? "输入化学式，如 Na^+（Enter 提交）"
            : "根据提示输入答案（Enter 提交）";
        _ignoreCase.IsVisible = !_plan.ChemicalAnswers;
        _progress.Text = "正在读取词表…";
        Opened += (_, _) => _readTask = LoadWordListAsync(_cancellation.Token);
        _closeButton.Click += async (_, _) => await CancelAndCloseAsync();
        _settleButton.Click += async (_, _) =>
        {
            if (_settlementTask is not null) return;
            _settlementTask = SettleSessionAsync();
            await _settlementTask;
        };
        _startButton.Click += (_, _) => StartPractice();
        _exportButton.Click += async (_, _) => await ExportCsvAsync();
        _translationsButton.Click += async (_, _) =>
        {
            if (_translationTask is { IsCompleted: false } || _acceptedAnswersTask is { IsCompleted: false }) return;
            _translationTask = EditTranslationsAsync();
            await _translationTask;
        };
        _acceptedAnswersButton.Click += async (_, _) =>
        {
            if (_acceptedAnswersTask is { IsCompleted: false } || _translationTask is { IsCompleted: false }) return;
            _acceptedAnswersTask = EditAcceptedAnswersAsync();
            await _acceptedAnswersTask;
        };
        _showTranslation.IsCheckedChanged += (_, _) =>
        {
            RefreshTranslationHint();
            if (_sessionTask is not null) SavePreferences();
            if (_phase == Phase.Typing) _answer.Focus();
        };
        // Capture Enter/KeyUp even when the textbox itself handles the key.
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(InputElement.KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _timer.Tick += OnTimerTick;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _allowClose = true;
            _externalCancellationRegistration.Dispose();
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _resultsList.ItemsSource = null;
            // An in-flight CSV export still needs the records until it finishes.
            _cancellation.Cancel();
            _cancellation.Dispose();
        };
        RestorePreferences();
        // Always post: an already-cancelled token must let ShowDialog open first.
        _externalCancellationRegistration = externalToken.Register(() =>
            Dispatcher.UIThread.Post(async () => await CancelAndCloseAsync()));
    }

    private async Task LoadWordListAsync(CancellationToken ct)
    {
        var actual = 0;
        var expected = 0;
        int? failedIndex = null;
        try
        {
            var round = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var source = _plan.Questions ?? throw new InvalidDataException("没有可用的练习题。");
                var count = source.Count;
                expected = _plan.UsesHostWordList && _host is IPracticeWordListMetadata metadata
                    ? metadata.ExpectedWordCount : count;
                if (expected <= 0) throw new InvalidDataException("词表没有可读取的词条。");
                int? invalidIndex = null;
                for (var index = 0; index < count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    failedIndex = index + 1;
                    var question = source[index];
                    if (question is null || string.IsNullOrWhiteSpace(question.Answer)
                        || question.Answer.Contains('\0') || question.Prompt is null
                        || question.Prompt.Contains('\0')
                        || !_plan.UsesHostWordList && string.IsNullOrWhiteSpace(question.Prompt))
                    {
                        invalidIndex ??= index + 1;
                        continue;
                    }
                    actual++;
                }
                failedIndex = invalidIndex;
                if (source.Count != count || actual != expected || invalidIndex is not null)
                    throw new InvalidDataException($"词表读取不完整：{actual} / {expected}。" +
                        (invalidIndex is { } invalid ? $"第 {invalid} 个词条为空或包含空字符。" : "词条数量与来源不一致。"));
                ct.ThrowIfCancellationRequested();
                var questions = _plan.CreateRoundQuestions(ct);
                var answers = Array.AsReadOnly(questions.Select(question => question.Answer).ToArray());
                return (Questions: questions, Answers: answers);
            }, ct);
            if (_closing) return;
            _questions = round.Questions;
            _words = round.Answers;
            _expectedWordCount = expected;
            _sessionProgress.Maximum = expected;
            _progress.Text = $"已读取 {actual} / {expected} 条";
            _wordListSummary.Text = DescribePlan();
            var hasChineseAnswers = !_plan.ChemicalAnswers
                && _questions.Any(question => ChineseAnswerEvaluation.ContainsChinese(question.Answer));
            _useAcceptedAnswers.IsVisible = _acceptedAnswersButton.IsVisible = hasChineseAnswers;
            if (hasChineseAnswers)
            {
                try
                {
                    _acceptedAnswers = PracticeAcceptedAnswers.Snapshot(await PracticeAcceptedAnswers.LoadAsync(ct));
                    _acceptedAnswersLoaded = true;
                    _acceptedAnswersButton.IsEnabled = true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    _useAcceptedAnswers.IsChecked = false;
                    _useAcceptedAnswers.IsEnabled = false;
                    _error.Text = "无法读取已确认答案，原文件已保留。本次仍可按标准答案严格练习。";
                }
            }
            try { _translations = await PracticeTranslations.LoadAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                _error.Text = "无法读取已保存的提示，可以继续练习。请检查插件数据目录中的 translations.json。";
                // Do not let editing overwrite an unreadable saved file with an empty map.
                _startButton.IsEnabled = true;
                return;
            }
            if (_closing) return;
            _translationsButton.IsEnabled = true;
            _startButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _phase = Phase.Error;
            _progress.Text = $"读取异常 · {actual} / {expected} 条";
            _phaseTitle.Text = "词表读取失败";
            _phaseDetail.Text = "请检查词表，再重新打开练习。";
            _setupPanel.IsVisible = false;
            _error.Text = ex.Message;
            _closeButton.Content = "关闭";
            await SaveCrashLogAsync("word-list-read", actual, expected, failedIndex, ex);
        }
    }

    private async Task EditTranslationsAsync()
    {
        if (_closing || _phase != Phase.Setup || _words.Count == 0) return;
        _translationsButton.IsEnabled = _startButton.IsEnabled = _acceptedAnswersButton.IsEnabled = false;
        try
        {
            var editor = new TranslationEditorWindow(_words, _translations, _translationEngines, _cancellation.Token);
            var saved = await editor.ShowDialog<Dictionary<string, string>?>(this);
            if (!_closing && saved is not null)
            {
                _translations = saved;
                var count = _words.Count(word => saved.ContainsKey(word));
                _wordListSummary.Text = DescribePlan() + $"\n已填写提示：{count} / {_words.Count} 条。";
                _error.Text = string.Empty;
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closing) _error.Text = $"无法打开提示编辑窗口：{ex.Message}";
        }
        finally
        {
            if (!_closing && _phase == Phase.Setup)
            {
                _translationsButton.IsEnabled = _startButton.IsEnabled = true;
                _acceptedAnswersButton.IsEnabled = _acceptedAnswersLoaded;
            }
        }
    }

    private async Task EditAcceptedAnswersAsync()
    {
        if (_closing || _phase != Phase.Setup || !_acceptedAnswersLoaded || _plan.ChemicalAnswers) return;
        _translationsButton.IsEnabled = _startButton.IsEnabled = _acceptedAnswersButton.IsEnabled = false;
        try
        {
            var editor = new AcceptedAnswersWindow(_questions, _acceptedAnswers, _cancellation.Token);
            var saved = await editor.ShowDialog<IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>>?>(this);
            if (!_closing && saved is not null)
            {
                _acceptedAnswers = PracticeAcceptedAnswers.Snapshot(saved);
                _useAcceptedAnswers.IsChecked = true;
                _error.Text = string.Empty;
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_closing) _error.Text = "无法打开可接受答案，请检查本地保存文件后重试。";
        }
        finally
        {
            if (!_closing && _phase == Phase.Setup)
                _translationsButton.IsEnabled = _startButton.IsEnabled = _acceptedAnswersButton.IsEnabled = true;
        }
    }

    private string DescribePlan() => $"来源：{_plan.SourceDescription}\n共 {_words.Count} 题 · "
        + (_plan.PlayAudio ? $"每题读 {_host.TimesPerWord} 遍，每遍间隔 {_host.IntervalDescription} 秒。"
            : "静音计时。")
        + (_plan.ShuffleQuestions ? "\n题目已打乱，本轮顺序保持不变。" : "\n按原顺序练习。")
        + (_plan.ChemicalAnswers ? "\n化学式的元素大小写和电荷都需要写对。" : string.Empty);

    private void RefreshTranslationHint()
    {
        var hasPrompt = _phase == Phase.Typing && _index < _questions.Count;
        _questionPrompt.Text = hasPrompt
            ? (_plan.PromptIsChemical ? ChemicalNotation.ToDisplay(_questions[_index].Prompt)
                : _questions[_index].Prompt) : string.Empty;
        _questionPrompt.IsVisible = !string.IsNullOrEmpty(_questionPrompt.Text);
        var show = _phase == Phase.Typing && _showTranslation.IsChecked == true
            && _index < _words.Count;
        _translationHint.Text = show && _translations.TryGetValue(_words[_index], out var translation)
            ? translation : string.Empty;
        _translationHint.IsVisible = !string.IsNullOrEmpty(_translationHint.Text);
    }

    private async Task SaveCrashLogAsync(string stage, int actual, int expected, int? failedIndex, Exception exception)
    {
        try
        {
            var path = await PracticeCrashLog.SaveAsync(stage, actual, expected, failedIndex, exception);
            if (!_allowClose) _error.Text += $"{Environment.NewLine}错误日志已保存：{path}";
        }
        catch (Exception logError)
        {
            Trace.WriteLine($"[SynchronousTyping] Crash log could not be saved: {logError}");
            if (!_allowClose) _error.Text += $"{Environment.NewLine}无法保存错误日志：{logError.Message}";
        }
    }

    private void StartPractice()
    {
        if (_closing || _sessionTask is not null || _translationTask is { IsCompleted: false }
            || _acceptedAnswersTask is { IsCompleted: false }
            || _phase != Phase.Setup || _words.Count == 0 ||
            _readTask is not { IsCompletedSuccessfully: true }) return;
        if (!int.TryParse(_seconds.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var secs)
            || secs is < 1 or > 300)
        {
            _error.Text = "每题用时请填写 1 到 300 之间的整数。";
            return;
        }

        _typingSeconds = secs;
        _ignoreCaseSnapshot = !_plan.ChemicalAnswers && _ignoreCase.IsChecked == true;
        _useAcceptedAnswersSnapshot = !_plan.ChemicalAnswers && _acceptedAnswersLoaded
            && _useAcceptedAnswers.IsChecked == true;
        _roundAcceptedAnswers = PracticeAcceptedAnswers.Snapshot(_acceptedAnswers);
        _error.Text = string.Empty;
        SavePreferences();
        _setupPanel.IsVisible = false;
        _sessionProgress.IsVisible = true;
        _startButton.IsEnabled = false;
        _closeButton.Content = "退出练习";
        _settleButton.IsVisible = true;
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
        _sessionTask = RunSessionAsync(_sessionCancellation.Token);
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        try
        {
            for (_index = 0; _index < _words.Count; _index++)
            {
                ct.ThrowIfCancellationRequested();
                var word = _words[_index];
                // The entire sequence (all repetitions plus every between-repeat
                // interval) remains in the "playing" phase. The awaited method
                // returns only when the last device playback completes.
                if (_plan.PlayAudio)
                {
                    await RepetitionPlayback.PlayAsync(word, Math.Clamp(_host.TimesPerWord, 1, 100),
                        _host.SpeakOnceAsync, _host.GetRepeatInterval, ShowPlaying, ct);
                }
                ct.ThrowIfCancellationRequested();
                var answer = await AwaitTypingAsync(ct);
                // Only record here. Do not compare and do not show correct text.
                var question = _questions[_index];
                _records.Add(new TypedResult(_index + 1, word, answer.Value,
                    answer.TimedOut, answer.Elapsed.TotalSeconds)
                {
                    Prompt = _plan.PromptIsChemical ? ChemicalNotation.ToDisplay(question.Prompt) : question.Prompt,
                    AnswerPromptKey = question.Prompt,
                    SourceLabel = question.SourceLabel
                });
                _sessionProgress.Value = _records.Count;
            }
            ct.ThrowIfCancellationRequested();
            _phase = Phase.Settling;
            _settleButton.IsEnabled = false;
            _phaseTitle.Text = "正在结算…";
            var correct = await Task.Run(() => TypingResults.Score(_records, _ignoreCaseSnapshot, ct,
                _plan.ChemicalAnswers, _roundAcceptedAnswers, _useAcceptedAnswersSnapshot), ct);
            ct.ThrowIfCancellationRequested();
            ShowFinalResults(correct);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested || ct.IsCancellationRequested)
        {
            // Window cancellation discards the session. Explicit early settlement
            // waits for this loop to stop, then scores the records separately.
        }
        catch (Exception ex)
        {
            _phase = Phase.Error;
            RefreshTranslationHint();
            _timer.Stop();
            _answer.IsVisible = false;
            _settleButton.IsVisible = false;
            _phaseTitle.Text = "练习出了点问题";
            _phaseDetail.Text = _plan.PlayAudio ? "请检查语音设置和音频输出，再试一次。"
                : "请检查练习内容，再试一次。";
            _error.Text = ex.Message;
            _closeButton.Content = "关闭";
            await SaveCrashLogAsync("practice", _words.Count, _expectedWordCount,
                _index < _words.Count ? _index + 1 : null, ex);
        }
        finally
        {
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
        }
    }

    private async Task SettleSessionAsync()
    {
        if (_closing || _settlementRequested || _sessionTask is not { IsCompleted: false }
            || _phase is not (Phase.Playing or Phase.Typing or Phase.Committing)) return;
        _settlementRequested = true;
        _settleButton.IsEnabled = false;
        // Keep a current nonblank answer, using the same commit path as Enter.
        // An untouched question and later questions are excluded from the score.
        if (_phase == Phase.Typing && !string.IsNullOrWhiteSpace(_answer.Text))
            CompleteAnswer(timedOut: false);
        _phase = Phase.Settling;
        _timer.Stop();
        _clock.Stop();
        _answer.IsVisible = false;
        _countdown.Text = string.Empty;
        RefreshTranslationHint();
        _phaseTitle.Text = "正在结算…";
        _phaseDetail.Text = "正在结束当前题目，请稍等。";
        var ct = _cancellation.Token;
        try
        {
            _sessionCancellation?.Cancel();
            await _sessionTask;
            if (_closing || _phase == Phase.Error) return;
            var correct = await Task.Run(() => TypingResults.Score(_records, _ignoreCaseSnapshot, ct,
                _plan.ChemicalAnswers, _roundAcceptedAnswers, _useAcceptedAnswersSnapshot), ct);
            ct.ThrowIfCancellationRequested();
            ShowFinalResults(correct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_closing) return;
            _phase = Phase.Error;
            _settleButton.IsVisible = false;
            _phaseTitle.Text = "无法结算成绩";
            _phaseDetail.Text = "请关闭练习后重试。";
            _error.Text = ex.Message;
            _closeButton.Content = "关闭";
            await SaveCrashLogAsync("settlement", _records.Count, _words.Count,
                _index < _words.Count ? _index + 1 : null, ex);
        }
    }

    private void ShowPlaying(int repeat, int maxRepeat, bool waiting)
    {
        _phase = Phase.Playing;
        RefreshTranslationHint();
        _progress.Text = $"第 {_index + 1} / {_words.Count} 题";
        _phaseTitle.Text = "正在播放";
        _phaseDetail.Text = waiting
            ? $"已读完第 {repeat} 遍，共 {maxRepeat} 遍。稍后继续…"
            : $"第 {repeat} / {maxRepeat} 遍";
        _answer.IsVisible = false;
        _countdown.IsVisible = true;
        _countdown.Text = "听完后开始答题";
    }

    private async Task<TypedAnswer> AwaitTypingAsync(CancellationToken ct)
    {
        _phase = Phase.Typing;
        RefreshTranslationHint();
        _progress.Text = $"第 {_index + 1} / {_words.Count} 题";
        _phaseTitle.Text = "请拼写";
        _phaseDetail.Text = _plan.PlayAudio ? "播报结束了，请输入答案。按 Enter 提交，到时自动提交。"
            : "请输入答案。按 Enter 提交，到时自动提交。";
        _answer.Text = string.Empty;
        _answer.IsVisible = true;
        _countdown.IsVisible = true;
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
            RefreshTranslationHint();
            _timer.Stop();
            _answer.IsVisible = false;
            _phaseTitle.Text = "已提交";
            _phaseDetail.Text = "准备下一题…";
            _countdown.Text = string.Empty;
        }
    }

    private void ShowFinalResults(int correct)
    {
        _phase = Phase.Completed;
        _timer.Stop();
        var count = _records.Count;
        var wrong = count - correct;
        _phaseTitle.Text = count == 0 ? "还没有作答" : $"正确率 {TypingEvaluation.Accuracy(correct, count):F1}%";
        _phaseDetail.Text = count == 0 ? "本次没有计分。"
            : $"已作答 {count} 题，答对 {correct} 题，答错或漏答 {wrong} 题。"
                + (count < _words.Count ? $"\n剩余 {_words.Count - count} 题未计分。" : string.Empty);
        RefreshTranslationHint();
        _progress.Text = count < _words.Count ? $"本次结算 {count} / {_words.Count} 题" : $"已完成 {count} 题";
        _countdown.Text = string.Empty;
        _answer.IsVisible = false;
        _exerciseScroll.IsVisible = false;
        _resultsList.ItemsSource = _records;
        _resultsView.IsVisible = true;
        _closeButton.Content = "关闭";
        _settleButton.IsVisible = false;
        _exportButton.IsVisible = count > 0;
    }

    private async Task ExportCsvAsync()
    {
        if (_phase != Phase.Completed || _csvSaving) return;
        _csvSaving = true;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出背诵练习成绩",
                SuggestedFileName = $"RecitationPractice_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV 表格") { Patterns = ["*.csv"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            await TypingResults.WriteCsvAsync(stream, _records);
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
        if (_acceptedAnswersTask is not null)
        {
            try { await _acceptedAnswersTask; }
            catch (OperationCanceledException) { }
        }
        if (_translationTask is not null)
        {
            try { await _translationTask; }
            catch (OperationCanceledException) { }
        }
        if (_readTask is not null)
        {
            try { await _readTask; }
            catch (OperationCanceledException) { }
        }
        if (_sessionTask is not null)
        {
            try { await _sessionTask; }
            catch (OperationCanceledException) { }
        }
        if (_settlementTask is not null)
        {
            try { await _settlementTask; }
            catch (OperationCanceledException) { }
        }
        // The owner may have closed this window while the session was stopping.
        if (!_allowClose)
        {
            _allowClose = true;
            Close();
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || (_readTask is null || _readTask.IsCompleted) &&
            (_sessionTask is null || _sessionTask.IsCompleted) &&
            (_settlementTask is null || _settlementTask.IsCompleted) &&
            (_acceptedAnswersTask is null || _acceptedAnswersTask.IsCompleted) &&
            (_translationTask is null || _translationTask.IsCompleted)) return;
        e.Cancel = true;
        await CancelAndCloseAsync();
    }

    private static string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
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
            _showTranslation.IsChecked = saved.ShowTranslation;
            _useAcceptedAnswers.IsChecked = saved.UseAcceptedChineseAnswers;
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
                TypingSeconds = _typingSeconds, IgnoreCase = _ignoreCase.IsChecked == true,
                ShowTranslation = _showTranslation.IsChecked == true,
                UseAcceptedChineseAnswers = _useAcceptedAnswers.IsChecked == true
            }));
        }
        catch (Exception) { /* The session still works if user config is read-only. */ }
    }

    private sealed class PracticePreferences
    {
        public PracticePreferences() { }
        public int TypingSeconds { get; set; } = 10;
        public bool IgnoreCase { get; set; } = true;
        public bool ShowTranslation { get; set; }
        public bool UseAcceptedChineseAnswers { get; set; } = true;
    }

    private sealed record TypedAnswer(string Value, bool TimedOut, TimeSpan Elapsed);
    private enum Phase { Setup, Playing, Typing, Committing, Settling, Completed, Error }
}
