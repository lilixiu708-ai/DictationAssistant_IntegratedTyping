// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: select paired questions and optional random order before practice.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.Practices.Typing;

internal sealed class PracticePreparationWindow : Window
{
    private readonly IPracticeHost _host;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellationRegistration;
    private readonly Button _openDocument = new() { Content = "从 Word / Excel 选择…", MinWidth = 180 };
    private readonly Button _useWords = new() { Content = "使用当前词表（播报听写）", MinWidth = 180 };
    private readonly TextBlock _source = new() { Text = "请选择文档，或使用当前词表。", TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _sections = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _promptColumn = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _answerColumn = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _swap = new() { Content = "交换提示与答案", MinWidth = 140 };
    private readonly CheckBox _skipHeaders = new() { Content = "跳过标题和表头", IsChecked = true };
    private readonly CheckBox _chemicalAnswers = new() { Content = "答案是化学式（区分元素大小写）" };
    private readonly CheckBox _shuffleQuestions = new() { Content = "打乱顺序", IsChecked = false };
    private readonly ComboBox _mode = new()
    {
        ItemsSource = new[] { "静音计时", "播报后作答" },
        SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    private readonly ListBox _preview = new() { MinHeight = 100 };
    private readonly StackPanel _documentOptions = new() { Spacing = 10, IsVisible = false };
    private readonly Button _next = new() { Content = "下一步：练习设置", MinWidth = 170, IsEnabled = false };
    private readonly Button _cancel = new() { Content = "取消", MinWidth = 90 };
    private ImportDocument? _document;
    private string? _fileName;
    private PracticeQuestionSet? _questionSet;
    private CancellationTokenSource? _previewCancellation;
    private Task? _previewTask;
    private Task? _operation;
    private bool _usingHostWords;
    private bool _populating;
    private bool _busy;
    private bool _closing;
    private bool _allowClose;

    public PracticePreparationWindow(IPracticeHost host, CancellationToken cancellationToken)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Title = "背诵练习 · 选择提示与答案";
        Width = 860;
        Height = 760;
        MinWidth = 640;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid
        {
            Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 12
        };
        Content = layout;
        layout.Children.Add(new TextBlock
        {
            Text = "先选提示和答案，再设置作答时间。默认看离子式，拼写英文名称。",
            FontSize = 19, TextWrapping = TextWrapping.Wrap
        });
        var content = new StackPanel { Spacing = 12 };
        var sourceButtons = new WrapPanel { ItemSpacing = 10, LineSpacing = 8 };
        sourceButtons.Children.Add(_openDocument);
        sourceButtons.Children.Add(_useWords);
        content.Children.Add(sourceButtons);
        content.Children.Add(_source);
        _documentOptions.Children.Add(Labeled("表格 / 工作表", _sections));
        _documentOptions.Children.Add(Labeled("提示列（显示在屏幕上）", _promptColumn));
        _documentOptions.Children.Add(Labeled("答案列（需要拼写）", _answerColumn));
        var row = new WrapPanel { ItemSpacing = 14, LineSpacing = 8 };
        row.Children.Add(_swap);
        row.Children.Add(_skipHeaders);
        _documentOptions.Children.Add(row);
        content.Children.Add(_documentOptions);
        content.Children.Add(Labeled("练习方式", _mode));
        content.Children.Add(_shuffleQuestions);
        content.Children.Add(_chemicalAnswers);
        content.Children.Add(new TextBlock
        {
            Text = "离子式可写成 Na^+、SO4^2-，也可直接输入上下标。\n打乱时，提示和答案一起移动，重复项也会保留。不勾选则按原顺序练习。",
            Opacity = 0.8, TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(_summary);
        content.Children.Add(new TextBlock { Text = "题目预览（原顺序，前 100 题，练习时隐藏答案）", FontWeight = FontWeight.SemiBold });
        content.Children.Add(_preview);
        var scroll = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        Grid.SetRow(_status, 2);
        layout.Children.Add(_status);
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10
        };
        footer.Children.Add(_next);
        footer.Children.Add(_cancel);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);

        _openDocument.Click += async (_, _) => await OpenDocumentAsync();
        _useWords.Click += (_, _) => UseHostWords();
        _sections.SelectionChanged += (_, _) =>
        {
            if (_populating) return;
            PopulateColumns();
            QueuePreview();
        };
        _promptColumn.SelectionChanged += (_, _) => { if (!_populating) QueuePreview(); };
        _answerColumn.SelectionChanged += (_, _) =>
        {
            if (_populating) return;
            InferChemicalAnswers();
            QueuePreview();
        };
        _skipHeaders.IsCheckedChanged += (_, _) => { if (!_populating) QueuePreview(); };
        _chemicalAnswers.IsCheckedChanged += (_, _) => { if (!_populating) RefreshPreviewList(); };
        _swap.Click += (_, _) => SwapColumns();
        _next.Click += async (_, _) => await AcceptAsync();
        _cancel.Click += (_, _) => Close();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _externalCancellationRegistration.Dispose();
            _cancellation.Cancel();
            _cancellation.Dispose();
            _preview.ItemsSource = null;
            _document = null;
            _questionSet = null;
        };
        UpdateControls();
        _externalCancellationRegistration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => Close()));
    }

    private static Control Labeled(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("165,*"), ColumnSpacing = 10 };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private async Task OpenDocumentAsync()
    {
        if (_busy || _closing) return;
        if (_host is not IPracticeDocumentHost documentHost)
        {
            _status.Text = "当前宿主不支持练习文档选择，请更新配套本体。";
            return;
        }
        _busy = true;
        UpdateControls();
        _status.Text = "正在选择或读取文档…";
        try
        {
            _operation = documentHost.OpenDocumentAsync(this, _cancellation.Token);
            var selected = await (Task<PracticeDocument?>)_operation;
            _cancellation.Token.ThrowIfCancellationRequested();
            if (selected is null) { _status.Text = string.Empty; return; }
            _usingHostWords = false;
            _document = selected.Document;
            _fileName = selected.FileName;
            _source.Text = "文档：" + _fileName;
            _mode.SelectedIndex = 0;
            _documentOptions.IsVisible = true;
            _populating = true;
            var sections = _document.Sections.Select((section, index) => new SectionChoice(index, section.Name))
                .Where(choice => _document.Sections[choice.Index].Kind == ImportSectionKind.Table).ToList();
            if (sections.Count > 1) sections.Insert(0, new SectionChoice(-1, "所有表格 / 工作表（按原顺序）"));
            _sections.ItemsSource = sections;
            _sections.SelectedItem = sections.FirstOrDefault(choice => choice.Index == _document.SuggestedSectionIndex)
                ?? sections.FirstOrDefault();
            _populating = false;
            PopulateColumns();
            QueuePreview();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closing) _status.Text = $"无法读取文档：{ex.Message}";
        }
        finally
        {
            _populating = false;
            _busy = false;
            if (!_closing) UpdateControls();
        }
    }

    private void UseHostWords()
    {
        if (_busy || _closing || _host.Words.Count == 0) return;
        _previewCancellation?.Cancel();
        _usingHostWords = true;
        _document = null;
        _documentOptions.IsVisible = false;
        _mode.SelectedIndex = 1;
        _chemicalAnswers.IsChecked = false;
        _questionSet = new PracticeQuestionSet(Array.AsReadOnly(_host.Words.Select((word, index) =>
            new PracticeQuestion(string.Empty, word, $"词表第 {index + 1} 项")).ToArray()), _host.Words.Count, 0, 0);
        _source.Text = $"当前词表共 {_host.Words.Count} 条，听完播报后作答。";
        _summary.Text = $"共 {_questionSet.Questions.Count} 题，重复项也会保留。";
        _status.Text = string.Empty;
        RefreshPreviewList();
        UpdateControls();
    }

    private void PopulateColumns()
    {
        _populating = true;
        try
        {
            _promptColumn.ItemsSource = _answerColumn.ItemsSource = null;
            if (_document is null || _sections.SelectedItem is not SectionChoice selected) return;
            var sections = PracticeQuestionBuilder.SelectSections(_document, selected.Index);
            var count = sections.Count == 0 ? 0 : sections.Max(section => section.Columns.Count);
            var choices = Enumerable.Range(0, count).Select(index => new ColumnChoice(index,
                $"第 {index + 1} 列 · " + string.Join(" / ", sections.Where(section => index < section.Columns.Count)
                    .Select(section => section.Columns[index]).Distinct(StringComparer.Ordinal).Take(3)))).ToArray();
            _promptColumn.ItemsSource = choices;
            _answerColumn.ItemsSource = choices;
            if (sections.Count == 0) return;
            var defaults = PracticeQuestionBuilder.GetDefaultColumns(sections[0]);
            _promptColumn.SelectedItem = choices.FirstOrDefault(choice => choice.Index == defaults.PromptColumn);
            _answerColumn.SelectedItem = choices.FirstOrDefault(choice => choice.Index == defaults.AnswerColumn);
            InferChemicalAnswers();
        }
        finally { _populating = false; }
    }

    private void InferChemicalAnswers()
    {
        _chemicalAnswers.IsChecked = _document is not null && _sections.SelectedItem is SectionChoice section
            && _answerColumn.SelectedItem is ColumnChoice column
            && PracticeQuestionBuilder.IsChemicalColumn(_document, section.Index, column.Index);
    }

    private void SwapColumns()
    {
        if (_populating || _busy) return;
        _populating = true;
        var prompt = _promptColumn.SelectedItem;
        _promptColumn.SelectedItem = _answerColumn.SelectedItem;
        _answerColumn.SelectedItem = prompt;
        InferChemicalAnswers();
        _populating = false;
        QueuePreview();
    }

    private void QueuePreview()
    {
        if (_usingHostWords || _closing) return;
        _previewCancellation?.Cancel();
        _questionSet = null;
        _preview.ItemsSource = null;
        _summary.Text = string.Empty;
        UpdateControls();
        if (_document is null || _sections.SelectedItem is not SectionChoice section)
        {
            _status.Text = "文档中没有可用的表格，请选择至少有两列的 Word 表格或 Excel 工作表。";
            return;
        }
        if (_promptColumn.SelectedItem is not ColumnChoice prompt || _answerColumn.SelectedItem is not ColumnChoice answer)
        {
            _status.Text = "请选择提示列和答案列，表格至少需要两列。";
            return;
        }
        _status.Text = "正在按行配对…";
        var previous = _previewTask;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
        _previewCancellation = cancellation;
        _previewTask = BuildPreviewAsync(previous, _document, section.Index, prompt.Index, answer.Index,
            _skipHeaders.IsChecked == true, cancellation);
    }

    private async Task BuildPreviewAsync(Task? previous, ImportDocument document, int section,
        int prompt, int answer, bool skipHeaders, CancellationTokenSource cancellation)
    {
        try
        {
            if (previous is not null) await previous;
            var token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            var set = await Task.Run(() => PracticeQuestionBuilder.Build(document, section, prompt, answer, skipHeaders, token), token);
            token.ThrowIfCancellationRequested();
            if (_closing || _usingHostWords) return;
            _questionSet = set;
            _summary.Text = $"共 {set.Questions.Count} 题；已跳过 {set.SkippedHeaders} 行表头，以及 {set.SkippedIncompleteRows} 行缺少提示或答案的内容。";
            _status.Text = set.Questions.Count == 0 ? "这两列没有可用的题目，请检查选列和“跳过标题和表头”设置。" : string.Empty;
            RefreshPreviewList();
            UpdateControls();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closing && !cancellation.IsCancellationRequested) _status.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            cancellation.Dispose();
        }
    }

    private void RefreshPreviewList()
    {
        var chemicalPrompt = !_usingHostWords && _document is not null
            && _sections.SelectedItem is SectionChoice section && _promptColumn.SelectedItem is ColumnChoice prompt
            && PracticeQuestionBuilder.IsChemicalColumn(_document, section.Index, prompt.Index);
        _preview.ItemsSource = _questionSet?.Questions.Take(100).Select((question, index) =>
            $"{index + 1}. {PreviewText(string.IsNullOrEmpty(question.Prompt) ? "（语音播报）" : chemicalPrompt ? ChemicalNotation.ToDisplay(question.Prompt) : question.Prompt)}  →  " +
            PreviewText(_chemicalAnswers.IsChecked == true ? ChemicalNotation.ToDisplay(question.Answer) : question.Answer)).ToArray();
    }

    private static string PreviewText(string value)
    {
        if (value.Length <= 200) return value;
        var length = char.IsHighSurrogate(value[199]) ? 199 : 200;
        return value[..length] + "…";
    }

    private void UpdateControls()
    {
        _openDocument.IsEnabled = !_busy && !_closing && _host is IPracticeDocumentHost;
        _useWords.IsEnabled = !_busy && !_closing && _host.Words.Count > 0;
        _documentOptions.IsEnabled = _chemicalAnswers.IsEnabled = !_busy && !_closing;
        _shuffleQuestions.IsEnabled = !_busy && !_closing;
        _mode.IsEnabled = !_busy && !_closing && !_usingHostWords;
        _next.IsEnabled = !_busy && !_closing && _questionSet?.Questions.Count > 0;
        _cancel.Content = _busy ? "取消并关闭" : "取消";
    }

    private async Task AcceptAsync()
    {
        if (_busy || _closing || _questionSet?.Questions.Count is not > 0) return;
        var token = _cancellation.Token;
        var promptIsChemical = !_usingHostWords && _document is not null
            && _sections.SelectedItem is SectionChoice section && _promptColumn.SelectedItem is ColumnChoice column
            && PracticeQuestionBuilder.IsChemicalColumn(_document, section.Index, column.Index);
        var plan = new PracticePlan(_questionSet.Questions, _usingHostWords || _mode.SelectedIndex == 1,
            promptIsChemical, _chemicalAnswers.IsChecked == true,
            _usingHostWords ? "当前主窗口词表" : _fileName ?? "已选文档", _usingHostWords,
            _shuffleQuestions.IsChecked == true);
        _busy = true;
        UpdateControls();
        try
        {
            // A canceled document preview may still be draining after selecting
            // the host word list. Finish that worker before disposing window state.
            _previewCancellation?.Cancel();
            if (_previewTask is not null) await _previewTask;
            token.ThrowIfCancellationRequested();
            if (_closing) return;
            _allowClose = true;
            Close(plan);
        }
        catch (OperationCanceledException) when (_closing || _cancellation.IsCancellationRequested) { }
        finally
        {
            _busy = false;
            if (!_closing) UpdateControls();
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        var pending = new[] { _operation, _previewTask }.Where(task => task is { IsCompleted: false }).Cast<Task>().ToArray();
        if (pending.Length == 0) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        UpdateControls();
        _cancellation.Cancel();
        try { await Task.WhenAll(pending); }
        catch (Exception) { /* Each operation observes cancellation and owns its error display. */ }
        _allowClose = true;
        Close();
    }

    private sealed record SectionChoice(int Index, string Name) { public override string ToString() => Name; }
    private sealed record ColumnChoice(int Index, string Name) { public override string ToString() => Name; }
}
