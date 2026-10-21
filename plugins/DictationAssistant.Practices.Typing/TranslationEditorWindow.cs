// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: prepare and save optional translations before practice.
// Modified 2026-10-21: cancel and drain owned configuration before closing.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DictationAssistant.Practices.Typing;

internal sealed class TranslationEditorWindow : Window
{
    private readonly IReadOnlyList<string> _words;
    private readonly TranslationEngineSession _engineSession;
    private readonly Dictionary<string, string> _saved;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellationRegistration;
    private readonly TextBox _editor = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
        MinHeight = 160, Watermark = "每行填写：原词（Tab）提示；也可从表格复制两列粘贴。"
    };
    private readonly Button _apiSettings = new()
    {
        Content = "管理翻译引擎…", MinWidth = 140
    };
    private readonly ComboBox _engineSelection = new() { MinWidth = 200, MaxWidth = 360 };
    private readonly TextBlock _apiSummary = new()
    {
        Text = "正在读取已绑定引擎…", TextWrapping = TextWrapping.Wrap
    };
    private readonly ComboBox _direction = new()
    {
        ItemsSource = new[] { "英文 → 中文", "中文 → 英文" }, SelectedIndex = 0, MinWidth = 140
    };
    private readonly Button _translate = new() { Content = "补全提示", MinWidth = 110 };
    private readonly Button _save = new() { Content = "保存提示", MinWidth = 100 };
    private readonly Button _cancel = new() { Content = "取消", MinWidth = 80 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private Task? _operation;
    private TranslationEngineConfiguration _engineConfiguration = TranslationEngineConfiguration.Empty;
    private bool _closing;
    private bool _allowClose;

    public TranslationEditorWindow(IReadOnlyList<string> words,
        IReadOnlyDictionary<string, string> saved, TranslationEngineSession engineSession,
        CancellationToken cancellationToken)
    {
        _words = words;
        _engineSession = engineSession;
        _saved = new Dictionary<string, string>(saved, StringComparer.Ordinal);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Title = "编辑提示";
        Width = 800;
        Height = 660;
        MinWidth = 620;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Let our Closing handler cancel and await work before closing owned dialogs.
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        _editor.Text = PracticeTranslations.BuildEditorText(words, saved);
        ScrollViewer.SetVerticalScrollBarVisibility(_editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_editor, ScrollBarVisibility.Auto);

        var layout = new Grid
        {
            Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"),
            RowSpacing = 12
        };
        Content = layout;
        layout.Children.Add(new TextBlock
        {
            Text = "左列保留原词，右列填写提示，用 Tab 隔开；也可从 Excel 复制两列粘贴。\n" +
                "提示留空即可删除，相同原词共用一条提示。原词仍用于播报和判分。",
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(_editor, 1);
        layout.Children.Add(_editor);

        var api = new StackPanel { Spacing = 8 };
        api.Children.Add(new TextBlock { Text = "用翻译补全提示（可选）", FontWeight = FontWeight.SemiBold });
        api.Children.Add(_apiSummary);
        var engineRow = new WrapPanel { ItemSpacing = 10, LineSpacing = 8 };
        engineRow.Children.Add(new TextBlock { Text = "本次翻译引擎：", VerticalAlignment = VerticalAlignment.Center });
        engineRow.Children.Add(_engineSelection);
        engineRow.Children.Add(_apiSettings);
        api.Children.Add(engineRow);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        options.Children.Add(_direction);
        options.Children.Add(_translate);
        api.Children.Add(options);
        api.Children.Add(new TextBlock
        {
            Text = "点击“补全提示”后，会将尚未填写提示的词条发给所选引擎，可能占用服务额度。已有提示会保留。\n" +
                "这里切换引擎只影响本次翻译；首选引擎请在插件配置中设置。保存前请检查提示。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });
        Grid.SetRow(api, 2);
        layout.Children.Add(api);
        Grid.SetRow(_status, 3);
        layout.Children.Add(_status);
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10
        };
        footer.Children.Add(_save);
        footer.Children.Add(_cancel);
        Grid.SetRow(footer, 4);
        layout.Children.Add(footer);

        _save.Click += async (_, _) => await RunOperationAsync(SaveAsync);
        _translate.Click += async (_, _) => await RunOperationAsync(TranslateAsync);
        _apiSettings.Click += async (_, _) => await RunOperationAsync(ConfigureApiAsync);
        Opened += async (_, _) => await RunOperationAsync(LoadApiAsync);
        _engineSelection.SelectionChanged += (_, _) => RefreshApiSummary();
        _cancel.Click += (_, _) => Close();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _externalCancellationRegistration.Dispose();
            _engineSelection.ItemsSource = null;
            _engineConfiguration = TranslationEngineConfiguration.Empty;
            _cancellation.Cancel();
            _cancellation.Dispose();
        };
        _externalCancellationRegistration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => Close()));
    }

    private async Task RunOperationAsync(Func<Task> action)
    {
        if (_closing || _operation is { IsCompleted: false }) return;
        SetBusy(true);
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
                // Service errors are sanitized by the clients and never include credentials/body.
                _status.Text = ex.Message;
            }
        }
        finally
        {
            if (!_closing) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _editor.IsReadOnly = busy;
        _save.IsEnabled = _apiSettings.IsEnabled = _direction.IsEnabled = !busy;
        _translate.IsEnabled = _engineSelection.IsEnabled = !busy && _engineConfiguration.Bindings.Count > 0;
        _cancel.Content = busy ? "取消并关闭" : "取消";
    }

    private async Task TranslateAsync()
    {
        var parsed = PracticeTranslations.ParseEditorText(_editor.Text ?? string.Empty, _words);
        var missing = _words.Distinct(StringComparer.Ordinal).Where(word => !parsed.ContainsKey(word)).ToArray();
        if (missing.Length == 0)
        {
            _status.Text = "所有词条都已有提示，无需翻译。";
            return;
        }
        var binding = _engineSelection.SelectedItem as TranslationEngineBinding
            ?? throw new InvalidOperationException("请先在插件配置中添加翻译引擎，也可以直接手动填写提示。");
        TranslationEngineProviders.Validate(binding);
        _status.Text = $"正在翻译 {missing.Length} 个词条，可点击“取消并关闭”停止…";
        var englishToChinese = _direction.SelectedIndex == 0;
        var translations = await TranslationService.TranslateAsync(missing, binding,
            englishToChinese ? "EN" : "ZH", englishToChinese ? "ZH" : "EN", _cancellation.Token);
        _cancellation.Token.ThrowIfCancellationRequested();
        for (var i = 0; i < missing.Length; i++) parsed[missing[i]] = translations[i];
        _editor.Text = PracticeTranslations.BuildEditorText(_words, parsed);
        _status.Text = $"已补全 {missing.Length} 条提示，检查后点击“保存提示”。";
    }

    private async Task LoadApiAsync()
    {
        _engineConfiguration = await _engineSession.GetAsync(_cancellation.Token);
        RefreshEngineSelection(null);
    }

    private async Task ConfigureApiAsync()
    {
        var selectedId = (_engineSelection.SelectedItem as TranslationEngineBinding)?.Id;
        var settings = new TranslationApiSettingsWindow(_engineSession, _cancellation.Token);
        await settings.ShowDialog(this);
        if (_closing) return;
        _engineConfiguration = await _engineSession.GetAsync(_cancellation.Token);
        RefreshEngineSelection(selectedId);
        _status.Text = string.Empty;
    }

    private void RefreshEngineSelection(string? selectedId)
    {
        _engineSelection.ItemsSource = _engineConfiguration.Bindings;
        _engineSelection.SelectedItem = _engineConfiguration.Bindings.FirstOrDefault(binding => binding.Id == selectedId)
            ?? _engineConfiguration.Preferred ?? _engineConfiguration.Bindings.FirstOrDefault();
        RefreshApiSummary();
    }

    private void RefreshApiSummary()
    {
        _apiSummary.Text = _engineConfiguration.Bindings.Count > 0
            ? $"已添加 {_engineConfiguration.Bindings.Count} 个配置；首选：{_engineConfiguration.Preferred?.Name ?? "未设置"}。"
            : "还没有添加翻译引擎，可先手动填写提示，或打开“管理翻译引擎”添加。";
    }

    private async Task SaveAsync()
    {
        var parsed = PracticeTranslations.ParseEditorText(_editor.Text ?? string.Empty, _words);
        var merged = new Dictionary<string, string>(_saved, StringComparer.Ordinal);
        foreach (var word in _words) merged.Remove(word);
        foreach (var pair in parsed) merged[pair.Key] = pair.Value;
        _status.Text = "正在保存提示…";
        await PracticeTranslations.SaveAsync(merged, _saved, _cancellation.Token);
        if (_closing) return;
        _allowClose = true;
        Close(merged);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || _operation is null || _operation.IsCompleted) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _cancellation.Cancel();
        try { await _operation; }
        catch (Exception) { /* The operation handler reports errors while the window is open. */ }
        _allowClose = true;
        Close();
    }
}
