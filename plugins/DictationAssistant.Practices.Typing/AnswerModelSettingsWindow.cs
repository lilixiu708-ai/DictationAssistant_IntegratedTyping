// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-21: configure the optional model used before answer review.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DictationAssistant.Practices.Typing;

internal sealed class AnswerModelSettingsWindow : Window
{
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellationRegistration;
    private readonly TextBox _endpoint = new() { MaxLength = 2048, Watermark = "https://api.openai.com/v1" };
    private readonly TextBox _model = new() { MaxLength = 256, Watermark = "填写服务商提供的模型名称" };
    private readonly TextBox _apiKey = new() { MaxLength = 4096, PasswordChar = '●' };
    private readonly Button _save = new() { MinWidth = 110 };
    private readonly Button _delete = new() { Content = "移除模型配置", MinWidth = 120 };
    private readonly Button _reset = new() { Content = "重新配置", IsVisible = false };
    private readonly Button _cancel = new() { Content = "取消", MinWidth = 80 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private AnswerModelConfiguration? _current;
    private Task? _operation;
    private bool _loaded;
    private bool _busy;
    private bool _closing;
    private bool _allowClose;

    public AnswerModelSettingsWindow(CancellationToken cancellationToken)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Title = "同义答案模型";
        Width = 720;
        Height = 580;
        MinWidth = 560;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _save.Content = AnswerModelSettings.CanPersist ? "保存配置" : "仅本次使用";
        var root = new Grid
        {
            Margin = new Thickness(22), RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 14
        };
        Content = root;
        var form = new StackPanel { Spacing = 10 };
        form.Children.Add(new TextBlock
        {
            Text = "模型只负责提供候选答案，经过你确认后才会参与本地判分。",
            FontSize = 18, TextWrapping = TextWrapping.Wrap
        });
        form.Children.Add(new TextBlock { Text = "接口地址（OpenAI 兼容接口的基础地址）" });
        form.Children.Add(_endpoint);
        form.Children.Add(new TextBlock { Text = "模型名称" });
        form.Children.Add(_model);
        form.Children.Add(new TextBlock { Text = "API 密钥" });
        form.Children.Add(_apiKey);
        form.Children.Add(new TextBlock
        {
            Text = "密钥不会显示。接口地址不变时，留空可沿用已保存的密钥；更换地址后请重新输入。\n" +
                "保存配置不会联网。只有点击“生成本页候选”才会发送该页的提示和标准答案；练习和结算不会调用模型。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });
        form.Children.Add(new TextBlock
        {
            Text = AnswerModelSettings.CanPersist
                ? "配置由 Windows 加密，绑定当前电脑和账户。"
                : "当前系统只在本次运行中使用配置，退出程序后不会保留。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });
        var scroll = new ScrollViewer
        {
            Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);
        var feedback = new StackPanel { Spacing = 8 };
        feedback.Children.Add(_status);
        feedback.Children.Add(_reset);
        Grid.SetRow(feedback, 1);
        root.Children.Add(feedback);
        var footer = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(_delete);
        footer.Children.Add(_save);
        footer.Children.Add(_cancel);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        _save.Click += async (_, _) => await RunOperationAsync(SaveAsync);
        _delete.Click += async (_, _) => await RunOperationAsync(DeleteAsync);
        _reset.Click += (_, _) =>
        {
            _current = null;
            _loaded = true;
            _endpoint.Text = AnswerModelSettings.DefaultEndpoint;
            _model.Text = _apiKey.Text = string.Empty;
            _reset.IsVisible = false;
            _status.Text = "填写并保存后才会替换旧配置；取消可保留原文件。";
            UpdateControls();
        };
        _cancel.Click += (_, _) => Close();
        Opened += async (_, _) => await RunOperationAsync(LoadAsync);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _externalCancellationRegistration.Dispose();
            _apiKey.Text = string.Empty;
            _current = null;
            _cancellation.Cancel();
            _cancellation.Dispose();
        };
        _endpoint.Text = AnswerModelSettings.DefaultEndpoint;
        UpdateControls();
        _externalCancellationRegistration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => Close()));
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
                _reset.IsVisible = !_loaded;
            }
        }
        finally
        {
            _busy = false;
            if (!_closing) UpdateControls();
        }
    }

    private async Task LoadAsync()
    {
        _current = await AnswerModelSettings.LoadAsync(_cancellation.Token);
        _cancellation.Token.ThrowIfCancellationRequested();
        if (_closing) return;
        _endpoint.Text = _current?.Endpoint ?? AnswerModelSettings.DefaultEndpoint;
        _model.Text = _current?.Model ?? string.Empty;
        _apiKey.Text = string.Empty;
        _apiKey.Watermark = _current is null ? "在本机输入密钥" : "已保存；留空沿用";
        _loaded = true;
    }

    private async Task SaveAsync()
    {
        var endpoint = (_endpoint.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(_apiKey.Text) && _current is not null && endpoint != _current.Endpoint)
            throw new InvalidOperationException("接口地址已更改，请重新输入对应服务的密钥。");
        var key = string.IsNullOrWhiteSpace(_apiKey.Text) ? _current?.ApiKey ?? string.Empty : _apiKey.Text.Trim();
        var configuration = new AnswerModelConfiguration(
            endpoint, (_model.Text ?? string.Empty).Trim(), key);
        AnswerModelSettings.Validate(configuration);
        await AnswerModelSettings.SaveAsync(configuration, _cancellation.Token);
        if (_closing) return;
        _allowClose = true;
        Close(true);
    }

    private async Task DeleteAsync()
    {
        await AnswerModelSettings.DeleteAsync(_cancellation.Token);
        if (_closing) return;
        _current = null;
        _endpoint.Text = AnswerModelSettings.DefaultEndpoint;
        _model.Text = _apiKey.Text = string.Empty;
        _apiKey.Watermark = "在本机输入密钥";
        _status.Text = "模型配置已移除。已确认的本地答案不会删除。";
    }

    private void UpdateControls()
    {
        _endpoint.IsEnabled = _model.IsEnabled = _apiKey.IsEnabled = !_busy && _loaded && !_closing;
        _save.IsEnabled = !_busy && _loaded && !_closing;
        _delete.IsEnabled = !_busy && _loaded && _current is not null && !_closing;
        _reset.IsEnabled = !_busy && !_closing;
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
}
