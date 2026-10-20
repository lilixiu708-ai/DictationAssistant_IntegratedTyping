// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: prepare and save optional translations before practice.
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
    private readonly Dictionary<string, string> _saved;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellationRegistration;
    private readonly TextBox _editor = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
        MinHeight = 160, Watermark = "每行：原词（Tab）译文；可从表格复制两列粘贴。"
    };
    private readonly Button _apiSettings = new()
    {
        Content = "翻译 API 设置…", MinWidth = 140
    };
    private readonly TextBlock _apiSummary = new()
    {
        Text = "尚未绑定翻译 API；手动录入不需要密钥。", TextWrapping = TextWrapping.Wrap
    };
    private readonly ComboBox _direction = new()
    {
        ItemsSource = new[] { "英文 → 中文", "中文 → 英文" }, SelectedIndex = 0, MinWidth = 140
    };
    private readonly Button _translate = new() { Content = "翻译缺失项", MinWidth = 110 };
    private readonly Button _save = new() { Content = "保存翻译", MinWidth = 100 };
    private readonly Button _cancel = new() { Content = "取消", MinWidth = 80 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private Task? _operation;
    private TranslationApiConfiguration? _apiConfiguration;
    private bool _closing;
    private bool _allowClose;

    public TranslationEditorWindow(IReadOnlyList<string> words,
        IReadOnlyDictionary<string, string> saved, CancellationToken cancellationToken)
    {
        _words = words;
        _saved = new Dictionary<string, string>(saved, StringComparer.Ordinal);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Title = "录入与提前翻译";
        Width = 800;
        Height = 660;
        MinWidth = 620;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
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
            Text = "每行保留原词，用 Tab 分隔译文；可从 Excel 等表格复制两列。\n" +
                "留空译文可移除提示，重复原词共用译文。原词仍用于播报和判分。",
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(_editor, 1);
        layout.Children.Add(_editor);

        var api = new StackPanel { Spacing = 8 };
        api.Children.Add(new TextBlock { Text = "可选：使用 DeepL 提前补全缺失译文", FontWeight = FontWeight.SemiBold });
        api.Children.Add(_apiSummary);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        options.Children.Add(_apiSettings);
        options.Children.Add(_direction);
        options.Children.Add(_translate);
        api.Children.Add(options);
        api.Children.Add(new TextBlock
        {
            Text = "仅点击翻译时将缺失项的原词发送至 DeepL，并可能消耗额度；已有译文不会覆盖。\n" +
                "请校对后保存。API 密钥请在独立的“翻译 API 设置”窗口绑定。",
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
        _cancel.Click += (_, _) => Close();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _externalCancellationRegistration.Dispose();
            _apiConfiguration = null;
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
                // DeepL errors are sanitized by the client and never include credentials/body.
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
        _save.IsEnabled = _translate.IsEnabled = _apiSettings.IsEnabled = _direction.IsEnabled = !busy;
        _cancel.Content = busy ? "取消并关闭" : "取消";
    }

    private async Task TranslateAsync()
    {
        var parsed = PracticeTranslations.ParseEditorText(_editor.Text ?? string.Empty, _words);
        var missing = _words.Distinct(StringComparer.Ordinal).Where(word => !parsed.ContainsKey(word)).ToArray();
        if (missing.Length == 0)
        {
            _status.Text = "所有词条已有译文，无需调用 DeepL。";
            return;
        }
        var configuration = _apiConfiguration
            ?? throw new InvalidOperationException("请先打开“翻译 API 设置”绑定 DeepL 密钥。手动录入和保存无需密钥。");
        _status.Text = $"正在提前翻译 {missing.Length} 个缺失项，可取消并关闭…";
        var englishToChinese = _direction.SelectedIndex == 0;
        var translations = await DeepLTranslator.TranslateAsync(missing, configuration.ApiKey, configuration.UseFreeApi,
            englishToChinese ? "EN" : "ZH", englishToChinese ? "ZH" : "EN", _cancellation.Token);
        _cancellation.Token.ThrowIfCancellationRequested();
        for (var i = 0; i < missing.Length; i++) parsed[missing[i]] = translations[i];
        _editor.Text = PracticeTranslations.BuildEditorText(_words, parsed);
        _status.Text = $"已补全 {missing.Length} 个译文，请校对后点击“保存翻译”。";
    }

    private async Task LoadApiAsync()
    {
        _apiConfiguration = await TranslationApiSettings.LoadAsync(_cancellation.Token);
        RefreshApiSummary();
    }

    private async Task ConfigureApiAsync()
    {
        var settings = new TranslationApiSettingsWindow(_apiConfiguration, _cancellation.Token);
        var configured = await settings.ShowDialog<TranslationApiConfiguration?>(this);
        if (_closing) return;
        if (settings.BindingDeleted) _apiConfiguration = null;
        if (configured is not null) _apiConfiguration = configured;
        RefreshApiSummary();
        _status.Text = settings.BindingDeleted ? "已删除本机 API 绑定。"
            : configured is not null ? "API 设置已更新，可提前翻译缺失项。" : string.Empty;
    }

    private void RefreshApiSummary() => _apiSummary.Text = _apiConfiguration is { } configured
        ? $"已配置 DeepL API {(configured.UseFreeApi ? "Free" : "Pro")}，可提前翻译缺失项。"
        : "尚未绑定翻译 API；手动录入不需要密钥。";

    private async Task SaveAsync()
    {
        var parsed = PracticeTranslations.ParseEditorText(_editor.Text ?? string.Empty, _words);
        var merged = new Dictionary<string, string>(_saved, StringComparer.Ordinal);
        foreach (var word in _words) merged.Remove(word);
        foreach (var pair in parsed) merged[pair.Key] = pair.Value;
        _status.Text = "正在保存翻译…";
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
