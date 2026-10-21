// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-21: review candidate aliases explicitly before offline scoring.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DictationAssistant.Practices.Typing;

internal sealed class AcceptedAnswersWindow : Window
{
    private const int PageSize = 16;
    private readonly IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> _saved;
    private readonly List<AnswerDraft> _drafts;
    private readonly List<(AnswerDraft Draft, TextBox Editor)> _visibleEditors = [];
    private readonly List<(CandidateDraft Draft, CheckBox Choice)> _visibleCandidates = [];
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellationRegistration;
    private readonly StackPanel _rows = new() { Spacing = 16 };
    private readonly Button _previous = new() { Content = "上一页" };
    private readonly Button _next = new() { Content = "下一页" };
    private readonly TextBlock _pageText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _generate = new() { Content = "生成本页候选", MinWidth = 120 };
    private readonly Button _modelSettings = new() { Content = "模型设置…", MinWidth = 110 };
    private readonly Button _save = new() { Content = "确认并保存", MinWidth = 120 };
    private readonly Button _cancel = new() { Content = "取消", MinWidth = 80 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private Task? _operation;
    private int _page;
    private bool _busy;
    private bool _closing;
    private bool _allowClose;

    public AcceptedAnswersWindow(IReadOnlyList<PracticeQuestion> questions,
        IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> saved, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(saved);
        _saved = PracticeAcceptedAnswers.Snapshot(saved);
        _drafts = questions.Select(question => new AnswerIdentity(question.Prompt, question.Answer))
            .Where(PracticeAcceptedAnswers.Supports).Distinct()
            .Select(identity => new AnswerDraft(identity, saved.TryGetValue(identity, out var aliases)
                ? string.Join(Environment.NewLine, aliases) : string.Empty)).ToList();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Title = "可接受答案";
        Width = 920;
        Height = 760;
        MinWidth = 660;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Let our Closing handler cancel and await work before closing owned dialogs.
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        var root = new Grid
        {
            Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), RowSpacing = 12
        };
        Content = root;
        root.Children.Add(new TextBlock
        {
            Text = "标准答案保持不变，你可以补充同义说法。\n模型候选默认不选中；请逐条核对，勾选后点击“确认并保存”，才会用于本地判分。\n生成本页候选会调用已配置的服务，可能占用账户额度。",
            FontSize = 17, TextWrapping = TextWrapping.Wrap
        });
        var tools = new WrapPanel { ItemSpacing = 10, LineSpacing = 8 };
        tools.Children.Add(_previous);
        tools.Children.Add(_pageText);
        tools.Children.Add(_next);
        tools.Children.Add(_generate);
        tools.Children.Add(_modelSettings);
        Grid.SetRow(tools, 1);
        root.Children.Add(tools);
        var scroll = new ScrollViewer
        {
            Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Grid.SetRow(_status, 3);
        root.Children.Add(_status);
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right
        };
        footer.Children.Add(_save);
        footer.Children.Add(_cancel);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        _previous.Click += (_, _) => ChangePage(-1);
        _next.Click += (_, _) => ChangePage(1);
        _generate.Click += async (_, _) => await RunOperationAsync(GenerateAsync);
        _modelSettings.Click += async (_, _) => await RunOperationAsync(ConfigureModelAsync);
        _save.Click += async (_, _) => await RunOperationAsync(SaveAsync);
        _cancel.Click += (_, _) => Close();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _externalCancellationRegistration.Dispose();
            _cancellation.Cancel();
            _cancellation.Dispose();
            _rows.Children.Clear();
            _visibleEditors.Clear();
            _visibleCandidates.Clear();
            _drafts.Clear();
        };
        RenderPage();
        _externalCancellationRegistration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => Close()));
    }

    private IEnumerable<AnswerDraft> CurrentPage => _drafts.Skip(_page * PageSize).Take(PageSize);

    private void ChangePage(int change)
    {
        if (_busy || _closing) return;
        CaptureEditors();
        _page = Math.Clamp(_page + change, 0, Math.Max(0, (_drafts.Count - 1) / PageSize));
        RenderPage();
    }

    private void RenderPage()
    {
        _rows.Children.Clear();
        _visibleEditors.Clear();
        _visibleCandidates.Clear();
        if (_drafts.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "当前没有可补充同义答案的中文题目。英文拼写和化学式仍按原规则判断。",
                TextWrapping = TextWrapping.Wrap
            });
        }
        foreach (var draft in CurrentPage)
        {
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new SelectableTextBlock
            {
                Text = "提示：" + (string.IsNullOrEmpty(draft.Identity.Prompt) ? "（当前词表）" : draft.Identity.Prompt),
                TextWrapping = TextWrapping.Wrap, Opacity = 0.85
            });
            content.Children.Add(new SelectableTextBlock
            {
                Text = "标准答案：" + draft.Identity.Expected,
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold
            });
            content.Children.Add(new TextBlock { Text = "已确认的其他答案（每行一个，留空可移除）" });
            var editor = new TextBox
            {
                Text = draft.ConfirmedText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 64, MaxHeight = 160, MaxLength = 5000,
                Watermark = "可手动填写，每题最多 8 个。"
            };
            editor.TextChanged += (_, _) => draft.ConfirmedText = editor.Text ?? string.Empty;
            _visibleEditors.Add((draft, editor));
            content.Children.Add(editor);
            if (draft.Candidates.Count > 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "模型候选（未勾选的不会保存）", FontWeight = FontWeight.SemiBold
                });
                foreach (var candidate in draft.Candidates)
                {
                    var choice = new CheckBox
                    {
                        IsChecked = candidate.Selected,
                        Content = new TextBlock { Text = candidate.Value, TextWrapping = TextWrapping.Wrap },
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    choice.IsCheckedChanged += (_, _) => candidate.Selected = choice.IsChecked == true;
                    _visibleCandidates.Add((candidate, choice));
                    content.Children.Add(choice);
                }
            }
            else if (draft.Generated)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "模型没有提供其他答案，也可以手动填写。", TextWrapping = TextWrapping.Wrap, Opacity = 0.75
                });
            }
            _rows.Children.Add(new Border
            {
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12), Child = content
            });
        }
        _pageText.Text = $"第 {_page + 1} / {Math.Max(1, (_drafts.Count + PageSize - 1) / PageSize)} 页，共 {_drafts.Count} 题";
        UpdateControls();
    }

    private async Task RunOperationAsync(Func<Task> action)
    {
        if (_closing || _busy) return;
        _busy = true;
        UpdateControls();
        _status.Foreground = Brushes.DimGray;
        try
        {
            _operation = action();
            await _operation;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _status.Foreground = Brushes.IndianRed;
                _status.Text = ex.Message;
            }
        }
        finally
        {
            _busy = false;
            if (!_closing) UpdateControls();
        }
    }

    private async Task GenerateAsync()
    {
        CaptureEditors();
        var page = CurrentPage.ToArray();
        var confirmed = page.Select(draft => ParseConfirmed(draft).ToHashSet(StringComparer.Ordinal)).ToArray();
        var configuration = await AnswerModelSettings.LoadAsync(_cancellation.Token)
            ?? throw new InvalidOperationException("请先打开“模型设置”填写接口、模型名称和密钥，也可以直接手动添加答案。");
        var requests = page.Select((draft, index) => new AnswerCandidateRequest(
            index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            draft.Identity.Prompt, draft.Identity.Expected)).ToArray();
        _status.Text = $"正在为本页 {requests.Length} 题生成候选，可取消并关闭…";
        var candidates = await AnswerCandidateService.GenerateAsync(requests, configuration, _cancellation.Token);
        _cancellation.Token.ThrowIfCancellationRequested();
        if (_closing) return;
        var staged = new List<CandidateDraft>[page.Length];
        for (var index = 0; index < page.Length; index++)
        {
            var draft = page[index];
            staged[index] = [];
            var id = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (candidates.TryGetValue(id, out var values))
            {
                foreach (var value in values.Distinct(StringComparer.Ordinal))
                {
                    if (value == draft.Identity.Expected || confirmed[index].Contains(value)) continue;
                    PracticeAcceptedAnswers.ValidateAlias(draft.Identity, value);
                    staged[index].Add(new CandidateDraft(value));
                }
            }
        }
        var total = staged.Sum(values => values.Count);
        for (var index = 0; index < page.Length; index++)
        {
            page[index].Candidates.Clear();
            page[index].Candidates.AddRange(staged[index]);
            page[index].Generated = true;
        }
        RenderPage();
        _status.Text = total > 0
            ? $"收到 {total} 条候选，尚未保存。请逐条勾选认可的答案，再点击“确认并保存”。"
            : "本页没有新的候选答案。可以手动填写，或保留标准答案。";
    }

    private async Task ConfigureModelAsync()
    {
        var settings = new AnswerModelSettingsWindow(_cancellation.Token);
        await settings.ShowDialog(this);
    }

    private static IReadOnlyList<string> ParseConfirmed(AnswerDraft draft)
    {
        var values = draft.ConfirmedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var value in values) PracticeAcceptedAnswers.ValidateAlias(draft.Identity, value);
        if (values.Length > PracticeAcceptedAnswers.MaximumAliases)
            throw new InvalidDataException($"“{draft.Identity.Expected}”最多可保存 {PracticeAcceptedAnswers.MaximumAliases} 个其他答案。");
        return values;
    }

    private async Task SaveAsync()
    {
        CaptureEditors();
        var merged = _saved.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var draft in _drafts)
        {
            var values = ParseConfirmed(draft).Concat(draft.Candidates.Where(candidate => candidate.Selected)
                .Select(candidate => candidate.Value)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var value in values) PracticeAcceptedAnswers.ValidateAlias(draft.Identity, value);
            if (values.Length > PracticeAcceptedAnswers.MaximumAliases)
                throw new InvalidDataException($"“{draft.Identity.Expected}”最多可保存 {PracticeAcceptedAnswers.MaximumAliases} 个其他答案，请减少勾选或手动填写的内容。");
            if (values.Length == 0) merged.Remove(draft.Identity);
            else merged[draft.Identity] = values;
        }
        var snapshot = PracticeAcceptedAnswers.Snapshot(merged);
        _status.Text = "正在保存已确认的答案…";
        await PracticeAcceptedAnswers.SaveAsync(snapshot, _saved, _cancellation.Token);
        if (_closing) return;
        _allowClose = true;
        Close(snapshot);
    }

    private void CaptureEditors()
    {
        // Avalonia may defer TextChanged until a later dispatcher turn. Read
        // the visible controls before saving or switching pages as well.
        foreach (var (draft, editor) in _visibleEditors)
            draft.ConfirmedText = editor.Text ?? string.Empty;
        foreach (var (draft, choice) in _visibleCandidates)
            draft.Selected = choice.IsChecked == true;
    }

    private void UpdateControls()
    {
        _rows.IsEnabled = !_busy && !_closing;
        _previous.IsEnabled = !_busy && !_closing && _page > 0;
        _next.IsEnabled = !_busy && !_closing && (_page + 1) * PageSize < _drafts.Count;
        _generate.IsEnabled = _save.IsEnabled = !_busy && !_closing && _drafts.Count > 0;
        _modelSettings.IsEnabled = !_busy && !_closing;
        _cancel.Content = _busy ? "取消并关闭" : "取消";
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || _operation is null || _operation.IsCompleted) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        UpdateControls();
        _cancellation.Cancel();
        try { await _operation; }
        catch (Exception) { /* The operation handler owns user-visible errors. */ }
        _allowClose = true;
        Close();
    }

    private sealed class AnswerDraft(AnswerIdentity identity, string confirmedText)
    {
        public AnswerIdentity Identity { get; } = identity;
        public string ConfirmedText { get; set; } = confirmedText;
        public List<CandidateDraft> Candidates { get; } = [];
        public bool Generated { get; set; }
    }

    private sealed class CandidateDraft(string value)
    {
        public string Value { get; } = value;
        public bool Selected { get; set; }
    }
}
